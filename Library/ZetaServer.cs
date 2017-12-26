using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Linq;
using System.Diagnostics;

// TODO: search for TODOs. 

namespace InvertedTomato.Zeta {
    public class ZetaServer : IDisposable {
        /// <summary>
        /// Underlying UDP socket
        /// </summary>
        private readonly Socket Socket;

        /// <summary>
        /// Thread sending packets.
        /// </summary>
        private readonly Thread SendThread;

        /// <summary>
        /// Thread processing acknowledgements.
        /// </summary>
        private readonly Thread ReceiveThread;

        /// <summary>
        /// Topics
        /// </summary>
        private readonly ConcurrentDictionary<UInt64, TopicRecord> TopicRecords = new ConcurrentDictionary<UInt64, TopicRecord>();
        private readonly ConcurrentDictionary<EndPoint, SubscriberRecord> SubscriberRecords = new ConcurrentDictionary<EndPoint, SubscriberRecord>();
        private readonly AutoResetEvent SendLock = new AutoResetEvent(true);
        private readonly Object Sync = new Object();

        private readonly Options Options;



        /// <summary>
        /// Are we disposed and no longer doing anything.
        /// </summary>
        public Boolean IsDisposed { get; private set; }

        public TimeSpan CurrentCoalesceDelay { get; private set; }


        public ZetaServer(UInt16 port) : this(new IPEndPoint(IPAddress.Any, port), new Options()) { }
        public ZetaServer(EndPoint endpoint) : this(endpoint, new Options()) { }

        /// <summary>
        /// Create a new server.
        /// </summary>
        /// <param name="endpoint">IP and port to listen on</param>
        /// <param name="options">Custom options</param>
        public ZetaServer(EndPoint endpoint, Options options) {
            if(null == options) {
                throw new ArgumentNullException(nameof(options));
            }

            // Store params
            Options = options;

            // Create and bind socket
            Socket = new Socket(SocketType.Dgram, ProtocolType.Udp);
            Socket.SendBufferSize = Options.SendBufferSize;
            Socket.ReceiveBufferSize = Options.ReceiveBufferSize;
            Socket.ReceiveTimeout = 1000; // Only so that we can actually land the plane neatly at shutdown time
            Socket.SendTimeout = 1; // The lowest possible value - if we're suffering from buffer backpressure we want to retry with fresh data later anyway
            Socket.DualMode = true;
            Socket.ExclusiveAddressUse = true;
            Socket.Ttl = options.Ttl;
            Socket.Bind(endpoint);

            // Start receiving
            ReceiveThread = new Thread(ReceieveThread_OnSpin);
            ReceiveThread.Start();

            // Start sending
            SendThread = new Thread(SendThread_OnSpin);
            SendThread.Start();
        }

        private void ReceieveThread_OnSpin(Object obj) {
            var buffer = new Byte[Options.Mtu];

            try {
                while(!IsDisposed) {
                    try {
                        // Remove all expired subscriptions  TODO - should be on a timer?
                        var expiry = DateTime.UtcNow.Subtract(Options.KeepAliveInterval).Subtract(Options.KeepAiveGrace);
                        foreach(var ep in SubscriberRecords.Where(a => a.Value.LastAuthorizedAt < expiry).Select(a => a.Key)) {
                            SubscriberRecords.TryRemove(ep, out var record);
                            Trace.WriteLine($"{ep} Subscription expired.", "server-receive");
                        }

                        // Wait for packet to arrive
                        var endpoint = (EndPoint)new IPEndPoint(IPAddress.Any, 0);
                        var len = Socket.ReceiveFrom(buffer, ref endpoint);
                        if(len < 1) {
                            Trace.WriteLine($"Strange byte count {len}.", "server-receive-warning");
                            continue;
                        }

                        // Check packet sanity
                        if(len < Constants.CLIENTTXHEADER_LENGTH) {
                            Trace.WriteLine($"{endpoint} Received packet that is too small to be valid. Discarded.", "server-receive");
                            continue;
                        }
                        if((len - Constants.CLIENTTXHEADER_LENGTH) % 10 > 0) {
                            Trace.WriteLine($"{endpoint} Received packet is not a valid length. Discarded.", "server-receive");
                            continue;
                        }

                        // Check version
                        var version = buffer[0];
                        if(version != Constants.VERSION) {
                            Trace.WriteLine($"{endpoint} Received packet version does not match or is corrupted. Discarded.", "server-receive");
                            continue;
                        }

                        // Check authorization token
                        var authorizationToken = new Byte[16];
                        Buffer.BlockCopy(buffer, 1, authorizationToken, 0, authorizationToken.Length);
                        if(!Options.AuthorizationFilter(endpoint, authorizationToken)) {
                            Trace.WriteLine($"{endpoint} Received packet with rejected authorization token. Discarded.", "server-receive");
                            continue;
                        }

                        // Find subscriber record
                        if(SubscriberRecords.TryGetValue(endpoint, out var subscriberRecord)) {
                            // Record exists, update authorizedAt
                            subscriberRecord.LastAuthorizedAt = DateTime.UtcNow;

                            // Process ACKs
                            var pos = Constants.CLIENTTXHEADER_LENGTH;
                            if(pos == len) {
                                Trace.WriteLine($"{endpoint} Sent keep-alive.", "server-receive");
                            }
                            while(pos < len) {
                                // Extract topic
                                var topic = BitConverter.ToUInt64(buffer, pos);

                                // Extract revision
                                var revision = BitConverter.ToUInt16(buffer, pos + 8);

                                Trace.WriteLine($"{endpoint} Acknowledged {topic}#{revision}.", "server-receive");

                                if(TopicRecords.TryGetValue(topic, out var topicRecord)) {
                                    if(topicRecord.Revision == revision) { // TODO Sync issue between this line and the one below!!!!!!
                                        topicRecord.PendingSubscribers.TryRemove(endpoint, out var a);
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
                            foreach(var topicRecord in TopicRecords.Select(a => a.Value)) {
                                topicRecord.PendingSubscribers[endpoint] = endpoint;
                            }
                        }
                    } catch(SocketException ex) {
                        if(ex.SocketErrorCode == SocketError.TimedOut) {
                            continue;
                        }
                        Trace.WriteLine($"Socket error {ex.SocketErrorCode}.", "server-receive-warning");
                    }
                }
            } catch(ObjectDisposedException) { }
        }

        private void SendThread_OnSpin(Object obj) {
            var stopwatch = new Stopwatch();
            try {
                while(!IsDisposed) {
                    try {
                        // Get current time
                        var now = DateTime.UtcNow;

                        // Start calculating max delay before next run
                        var next = Options.RetransmitInterval;

                        // Start timing run
                        stopwatch.Restart();

                        // For each topic...
                        foreach(var item in TopicRecords) {
                            var record = item.Value;

                            // If the topic is due to be published...
                            var delta = record.SendAfter - now;
                            if(delta.TotalMilliseconds < 0) {
                                // Update the send-after for retransmits
                                record.SendAfter = now.Add(Options.RetransmitInterval);

                                // Send to each client..
                                foreach(var endpoint in record.PendingSubscribers.Values) {
                                    var len = Socket.SendTo(record.Packet, endpoint);
                                    Trace.WriteLineIf(len != record.Packet.Length, $"Strange byte count {len}.", "server-send-warning");
                                }
                            } else if(delta < next) {
                                next = delta;
                            }
                        }

                        // Capture runtime
                        stopwatch.Stop();
                        CurrentCoalesceDelay = stopwatch.Elapsed;

                        // Wait until there is a new publish, or there is likely  for a republish to be required
                        SendLock.WaitOne(next);
                    } catch(SocketException ex) {
                        Trace.WriteLine($"Socket error {ex.SocketErrorCode}.", "server-receive-warning");
                    }
                }
            } catch(ObjectDisposedException) { }
        }

        public void Publish(UInt64 topic, Byte[] value) {
            // Handle un-publishes
            if(null == value) {
                TopicRecords.TryRemove(topic, out var a);
                return;
            }

            Trace.WriteLineIf(value.Length + Constants.SERVERTXHEADER_LENGTH > Options.Mtu, $"Publish value length exceeds MTU set in options and will probably be dropped by the network. Sending anyway. ([Header]{Constants.SERVERTXHEADER_LENGTH} + [Value]{value.Length} > [MTU]{Options.Mtu})", "server-publish-warning");

            lock(Sync) {
                if(IsDisposed) {
                    throw new ObjectDisposedException("Object disposed.");
                }

                // Get topic, or create if needed
                if(TopicRecords.TryGetValue(topic, out var record)) {
                    record.Revision++;
                } else {
                    record = TopicRecords[topic] = new TopicRecord() {
                        PendingSubscribers = new ConcurrentDictionary<EndPoint, EndPoint>() // Required to avoid sync issue over the following few lines
                    };
                }

                // Compose packet
                record.Packet = new Byte[Constants.SERVERTXHEADER_LENGTH + value.Length];
                Buffer.BlockCopy(BitConverter.GetBytes(topic), 0, record.Packet, 0, 8);             // UInt64 topic
                Buffer.BlockCopy(BitConverter.GetBytes(record.Revision), 0, record.Packet, 8, 2);   // UInt16 revision
                Buffer.BlockCopy(value, 0, record.Packet, 10, value.Length);                        // Byte[?] value

                // Reset subscriber lists
                record.PendingSubscribers = new ConcurrentDictionary<EndPoint, EndPoint>(SubscriberRecords.ToDictionary(a => a.Key, a => a.Key));
                record.SendAfter = DateTime.MinValue;

                // Release lock
                SendLock.Set();
            }
        }

        protected virtual void Dispose(Boolean disposing) {
            lock(Sync) {
                if(IsDisposed) {
                    return;
                }
                IsDisposed = true;

                if(disposing) {
                    // Dispose managed state (managed objects)
                    SendThread?.Join();
                    ReceiveThread?.Join();
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
