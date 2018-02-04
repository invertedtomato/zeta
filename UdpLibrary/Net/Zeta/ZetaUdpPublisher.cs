using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using InvertedTomato.IO.Messages;

namespace InvertedTomato.Net.Zeta {
    /// <summary>
    /// Publish messages via UDP in the format CCCCTTTTRRP*
    ///   CCCC  typecode used to distinguish the message type needed for decoding
    ///   TTTT  topic used to distinguish what this message relates to (not always needed)
    ///   RR    revision
    ///         (length not required, as it's implied)
    ///   P*    payload
    /// </summary>
    public class ZetaUdpPublisher : IDisposable {
        /// <summary>
        /// Are we disposed and no longer doing anything.
        /// </summary>
        public Boolean IsDisposed { get; private set; }

        public Boolean IsStarted { get; private set; }

        /// <summary>
        /// Maximum time taken pass published values to the operating system for transmission.
        /// </summary>
        public TimeSpan CurrentCoalesceDelay { get; private set; }

        /// <summary>
        /// The maximum packet size to send, and to expect to receive.
        /// </summary>
        public Int32 Mtu { get; set; } = 520;

        /// <summary>
        /// The maxium amount of received bytes to buffer before dropping packets.
        /// </summary>
        /// <remarks>
        /// The bigger this is the higher the potential latency. Current thinking is 2*Mtu is a good value
        /// </remarks>
        public Int32 ReceiveBufferSize { get { return Socket.ReceiveBufferSize; } set { Socket.ReceiveBufferSize = value; } }

        /// <summary>
        /// The maximum number of sending bytes to buffer before sending is halted.
        /// </summary>
        /// <remarks>
        /// The bigger this is the higher the potential latency. Current thinking is 2*Mtu is a good value
        /// </remarks>
        public Int32 SendBufferSize { get { return Socket.SendBufferSize; } set { Socket.SendBufferSize = value; } }

        /// <summary>
        /// Token for the client to send to identify itself. Must be 16 bytes.
        /// </summary>
        /// <remarks>
        /// Leave as all zeros if not required.
        /// </remarks>
        public Byte[] AuthorizationToken { get; set; } = new Byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

        /// <summary>
        /// Method for server to accept or reject client subscription requests.
        /// </summary>
        /// <remarks>
        /// This is called very often, so use caching if required.
        /// </remarks>
        public Func<EndPoint, Byte[], Boolean> AuthorizationFilter { get; set; } = (endpoint, token) => true;

        /// <summary>
        /// The client must send a packet to the server at least every interval.
        /// </summary>
        /// <remarks>
        /// Lower values allow detecting disconnected clients quicker, however consumes more data. Values musn't be too low however or (dump) NAT routers may consider the stream closed and start dropping packets. If your network uses NAT, I suggest 15 sec maximum!
        /// </remarks>
        public TimeSpan KeepAliveInterval { get; set; } = new TimeSpan(0, 0, 5);

        /// <summary>
        /// The amount of time past the keep-alive interval for the server to keep sending data to a client. The client will be disconnected when both the KeepAliveInterval and KeepAliveGrace have elapsed.
        /// </summary>
        /// <remarks>
        /// This is to stop clients getting disconnected when there is packet loss on the network. Current thinking is that it should be 2*KeepAliveInterval.
        /// </remarks>
        public TimeSpan KeepAiveGrace { get; set; } = new TimeSpan(0, 0, 0, 10);

        /// <summary>
        /// If a subscriber hasn't acnowledged recept of a value, retransmit it after this period.
        /// </summary>
        /// <remarks>
        /// This should be at least the round-trip-time on the network.
        /// </remarks>
        public TimeSpan RetransmitInterval { get; set; } = new TimeSpan(0, 0, 0, 0, 250);

        /// <summary>
        /// Time to live - the maximum number of router hops the packet can transverse before being dropped.
        /// </summary>
        /// <remarks>
        /// Only change this is you have a specific reason and you know what you're doing.
        /// </remarks>
        public Int16 Ttl { get { return Socket.Ttl; } set { Socket.Ttl = value; } }

        private Socket Socket;
        private readonly Thread SendThread;
        private readonly Thread ReceiveThread;
        private readonly ConcurrentDictionary<UInt32, TopicRecord> TopicRecords = new ConcurrentDictionary<UInt32, TopicRecord>();
        private readonly ConcurrentDictionary<EndPoint, SubscriberRecord> SubscriberRecords = new ConcurrentDictionary<EndPoint, SubscriberRecord>();
        private readonly AutoResetEvent SendLock = new AutoResetEvent(true);
        private readonly Object Sync = new Object();

        public ZetaUdpPublisher() {
            Socket = new Socket(SocketType.Dgram, ProtocolType.Udp) {
                ReceiveTimeout = 1000, // Only so that we can actually land the plane neatly at shutdown time
                SendTimeout = 1, // The lowest possible value - if we're suffering from buffer backpressure we want to retry with fresh data later anyway
                DualMode = true,
                ExclusiveAddressUse = true,
                SendBufferSize = 1040,
                ReceiveBufferSize = 1040
            };
            ReceiveThread = new Thread(ReceieveThread_OnSpin);
            SendThread = new Thread(SendThread_OnSpin);
        }

        public void Start(UInt16 port) {
            Start(new IPEndPoint(IPAddress.Any, port));
        }
        public void Start(EndPoint endpoint) {
            lock (Sync) {
                if (IsStarted) {
                    throw new InvalidOperationException("Already started.");
                }

                // Bind socket
                Socket.Bind(endpoint);

                IsStarted = true;

                // Start receiving
                ReceiveThread.Start();

                // Start sending
                SendThread.Start();
            }
        }


        /// <summary>
        /// Publish a message to clients on the default topic (0).
        /// </summary>
        public void Publish<TMessage>(TMessage message) where TMessage : IExportableMessage, new() {
            Publish(message, 0);
        }

        /// <summary>
        /// Publish a message to clients.
        /// </summary>
        public void Publish<TMessage>(TMessage message, UInt32 topic) where TMessage : IExportableMessage, new() {
            // Handle un-publishes
            if (null == message) {
                TopicRecords.TryRemove(topic, out var a);
                return;
            }

            // Extract typecode
            var typeCode = message.TypeCode;

            // Extract payload
            var payload = message.Export();

            Trace.WriteLineIf(payload.Count + Constants.SERVERTXHEADER_LENGTH > Mtu, $"Publish value length exceeds MTU set in options and will probably be dropped by the network. Sending anyway. ([Header]{Constants.SERVERTXHEADER_LENGTH} + [Value]{payload.Count} > [MTU]{Mtu})", "server-publish-warning");

            lock (Sync) {
                if (IsDisposed) {
                    throw new ObjectDisposedException("Object disposed.");
                }

                if (!IsStarted) {
                    throw new InvalidOperationException("Not yet started.");
                }

                var record = new TopicRecord(); // Note that SendAfter initializes as '0000-00-00 00:00:00'

                // Calculate new revision
                if (TopicRecords.TryGetValue(topic, out var lastRecord)) {
                    record.Revision = lastRecord.Revision;
                } else {
                    record.Revision = UInt16.MaxValue;
                }
                record.Revision++;

                // Add all subscribers as pending recipents
                record.PendingSubscribers = SubscriberRecords.Select(a => a.Key).ToArray();

                // Compose packet
                record.Packet = new Byte[Constants.SERVERTXHEADER_LENGTH + payload.Count];
                Buffer.BlockCopy(BitConverter.GetBytes(typeCode), 0, record.Packet, 0, 4);          // UInt32 typecode
                Buffer.BlockCopy(BitConverter.GetBytes(topic), 0, record.Packet, 4, 4);             // UInt32 topic
                Buffer.BlockCopy(BitConverter.GetBytes(record.Revision), 0, record.Packet, 8, 2);   // UInt16 revision
                Buffer.BlockCopy(payload.Array, payload.Offset, record.Packet, 10, payload.Count);   // Byte[?] value

                // Release topic update
                TopicRecords[topic] = record;
                SendLock.Set();
            }
        }

        private void ReceieveThread_OnSpin(Object obj) {
            var buffer = new Byte[Mtu];

            try {
                while (!IsDisposed) {
                    try {
                        // Remove all expired subscriptions  TODO - should be on a timer?
                        var expiry = DateTime.UtcNow.Subtract(KeepAliveInterval).Subtract(KeepAiveGrace);
                        foreach (var ep in SubscriberRecords.Where(a => a.Value.LastAuthorizedAt < expiry).Select(a => a.Key)) {
                            SubscriberRecords.TryRemove(ep, out var record);
                            Trace.WriteLine($"{ep} Subscription expired.", "server-receive");
                        }

                        // Wait for packet to arrive
                        var endpoint = (EndPoint)new IPEndPoint(IPAddress.Any, 0);
                        var len = Socket.ReceiveFrom(buffer, ref endpoint);
                        if (len < 1) {
                            Trace.WriteLine($"Strange byte count {len}.", "server-receive-warning");
                            continue;
                        }

                        // Check packet sanity
                        if (len < Constants.CLIENTTXHEADER_LENGTH) {
                            Trace.WriteLine($"{endpoint} Received packet that is too small to be valid. Discarded.", "server-receive");
                            continue;
                        }
                        if ((len - Constants.CLIENTTXHEADER_LENGTH) % 6 > 0) {
                            Trace.WriteLine($"{endpoint} Received packet is not a valid length. Discarded.", "server-receive");
                            continue;
                        }

                        // Check version
                        var version = buffer[0];
                        if (version != Constants.VERSION) {
                            Trace.WriteLine($"{endpoint} Received packet version does not match or is corrupted. Discarded.", "server-receive");
                            continue;
                        }

                        // Check authorization token
                        var authorizationToken = new Byte[16];
                        Buffer.BlockCopy(buffer, 1, authorizationToken, 0, authorizationToken.Length);
                        if (!AuthorizationFilter(endpoint, authorizationToken)) {
                            Trace.WriteLine($"{endpoint} Received packet with rejected authorization token. Discarded.", "server-receive");
                            continue;
                        }

                        // Find subscriber record
                        if (SubscriberRecords.TryGetValue(endpoint, out var subscriberRecord)) {
                            // Record exists, update authorizedAt
                            subscriberRecord.LastAuthorizedAt = DateTime.UtcNow;

                            // Process ACKs
                            var pos = Constants.CLIENTTXHEADER_LENGTH;
                            if (pos == len) {
                                Trace.WriteLine($"{endpoint} Sent keep-alive.", "server-receive");
                            }
                            while (pos < len) {
                                // Extract topic
                                var topic = BitConverter.ToUInt32(buffer, pos);

                                // Extract revision
                                var revision = BitConverter.ToUInt16(buffer, pos + 4);

                                Trace.WriteLine($"{endpoint} Acknowledged {topic}#{revision}.", "server-receive");

                                if (TopicRecords.TryGetValue(topic, out var topicRecord)) {
                                    if (topicRecord.Revision == revision) {
                                        topicRecord.PendingSubscribers = topicRecord.PendingSubscribers.Except(new EndPoint[] { endpoint }).ToArray(); // Replace rather than adding so we don't have a sync issue
                                    }
                                }

                                pos += 10;
                            }
                        } else {
                            // Record doesn't exist, created
                            subscriberRecord = SubscriberRecords[endpoint] = new SubscriberRecord() {
                                LastAuthorizedAt = DateTime.UtcNow,
                            };

                            // Queue sending latest value from all topics
                            foreach (var topicRecord in TopicRecords.Select(a => a.Value)) {
                                topicRecord.PendingSubscribers = topicRecord.PendingSubscribers.Union(new EndPoint[] { endpoint }).ToArray(); // Replace rather than adding so we don't have a sync issue
                            }
                        }
                    } catch (SocketException ex) {
                        if (ex.SocketErrorCode == SocketError.TimedOut) {
                            continue;
                        }
                        Trace.WriteLine($"Socket error {ex.SocketErrorCode}.", "server-receive-warning");
                    }
                }
            } catch (ObjectDisposedException) { }
        }

        private void SendThread_OnSpin(Object obj) {
            var stopwatch = new Stopwatch();
            try {
                while (!IsDisposed) {
                    try {
                        // Get current time
                        var now = DateTime.UtcNow;

                        // Start calculating max delay before next run
                        var next = RetransmitInterval;

                        // Start timing run
                        stopwatch.Restart();

                        // For each topic...
                        foreach (var item in TopicRecords) {
                            var record = item.Value;

                            // If the topic is due to be published...
                            var delta = record.SendAfter - now;
                            if (delta.TotalMilliseconds < 0) {
                                // Update the send-after for retransmits
                                record.SendAfter = now.Add(RetransmitInterval);

                                // Send to each client..
                                foreach (var endpoint in record.PendingSubscribers) {
                                    var len = Socket.SendTo(record.Packet, endpoint);
                                    Trace.WriteLineIf(len != record.Packet.Length, $"Strange byte count {len}.", "server-send-warning");
                                }
                            } else if (delta < next) {
                                next = delta;
                            }
                        }

                        // Capture runtime
                        stopwatch.Stop();
                        CurrentCoalesceDelay = stopwatch.Elapsed;

                        // Wait until there is a new publish, or there is likely  for a republish to be required
                        SendLock.WaitOne(next);
                    } catch (SocketException ex) {
                        Trace.WriteLine($"Socket error {ex.SocketErrorCode}.", "server-receive-warning");
                    }
                }
            } catch (ObjectDisposedException) { }
        }

        protected virtual void Dispose(Boolean disposing) {
            lock (Sync) {
                if (IsDisposed) {
                    return;
                }
                IsDisposed = true;

                if (disposing) {
                    // Dispose managed state (managed objects)
                    var sendThread = SendThread;
                    if (sendThread != null && sendThread.IsAlive) {
                        sendThread.Join();
                    }
                    var receiveThread = ReceiveThread;
                    if (receiveThread != null && receiveThread.IsAlive) {
                        receiveThread.Join();
                    }
                    Socket?.Shutdown(SocketShutdown.Both);
                    Socket?.Dispose();
                }

                // Set large fields to null
            }
        }

        public void Dispose() {
            Dispose(true);
        }
    }
}
