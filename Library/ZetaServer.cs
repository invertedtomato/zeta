using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Linq;

namespace InvertedTomato.Zeta {
    public class ZetaServer : IDisposable {
        private readonly Func<EndPoint, Byte[], Boolean> SubscriberFilter;

        private readonly Socket Socket;
        private readonly Thread PublishThread;
        private readonly ConcurrentDictionary<UInt64, TopicRecord> TopicRecords = new ConcurrentDictionary<UInt64, TopicRecord>();
        private readonly ConcurrentDictionary<EndPoint, ClientRecord> SubscriberRecords = new ConcurrentDictionary<EndPoint, ClientRecord>();
        private readonly AutoResetEvent SpinLock = new AutoResetEvent(true);
        private readonly Object Sync = new Object();

        private static readonly UInt16 ReceiveBufferSize = 1500;
        private static readonly TimeSpan ExpectedAnnounceInterval = new TimeSpan(0, 0, 15);

        /// <summary>
        /// If a topic hasn't changed in this interval, retransmit it's value anyway.
        /// </summary>
        /// <remarks>
        /// Useful if there's packet loss and the last publish was lost.
        /// </remarks>
        public TimeSpan RepublishInterval { get; set; } = new TimeSpan(0, 0, 15);

        /// <summary>
        /// Are we disposed and no longer doing anything.
        /// </summary>
        public Boolean IsDisposed { get; private set; }


        public event Action<SocketError> OnSocketError;

        public ZetaServer(UInt16 port) : this(port, (endpoint, token) => { return true; }) { }

        public ZetaServer(UInt16 port, Func<EndPoint, Byte[], Boolean> subscriberFilter) {
            if(null == subscriberFilter) {
                throw new ArgumentNullException(nameof(subscriberFilter));
            }

            // Store params
            SubscriberFilter = subscriberFilter;

            // Create and bind socket
            Socket = new Socket(SocketType.Dgram, ProtocolType.Udp);
            //Socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.PacketInformation, true);
            Socket.Bind(new IPEndPoint(IPAddress.Any, port));

            // Seed receiving
            ReceiveAnnounce(); // Only need one thread

            PublishThread = new Thread(PublishThread_OnSpin);
            PublishThread.Start();
        }

        private void PublishThread_OnSpin(Object obj) {
            try {
                while(!IsDisposed) {
                    // Get list of active subscriber endpoints
                    var subscriptionEndpoints = SubscriberRecords.Where(a => a.Value.SubscribedUntil >= DateTime.UtcNow).Select(a => a.Key).ToArray();

                    // For each topic...
                    foreach(var item in TopicRecords) {
                        var record = item.Value;

                        // If the topic is due to be published...
                        if(record.LastSentAt <= DateTime.UtcNow.Subtract(RepublishInterval)) {
                            // Send to each client..
                            foreach(var endpoint in subscriptionEndpoints) {
                                Socket.SendTo(record.Packet, endpoint);
                            }

                            // Update LastSent
                            record.LastSentAt = DateTime.UtcNow;
                        }
                    }

                    // Wait until there is a new publish, or there is likely  for a republish to be required
                    SpinLock.WaitOne((Int32)(RepublishInterval.TotalMilliseconds / 2));
                }
            } catch(ObjectDisposedException) { }
        }

        private void ReceiveAnnounce() {
            try {
                var socketEventArg = new SocketAsyncEventArgs();
                socketEventArg.RemoteEndPoint = new IPEndPoint(IPAddress.Any, 0); // Required in conjunction with ReceiveMessageFromAsync - ReceiveAsync does not report remote endpoint
                socketEventArg.SetBuffer(new Byte[ReceiveBufferSize], 0, ReceiveBufferSize);
                socketEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(delegate (Object s, SocketAsyncEventArgs e) {
                    if(IsDisposed) {
                        return;
                    }

                    if(e.SocketError != SocketError.Success) {
                        OnSocketError(e.SocketError);
                    } else {
                        // Extract authorization
                        var authorization = new Byte[e.BytesTransferred];
                        Buffer.BlockCopy(e.Buffer, 0, authorization, 0, authorization.Length);

                        // Check subscriber is approved
                        if(SubscriberFilter(e.RemoteEndPoint, authorization)) {
                            // Create new record if needed
                            var renewal = true;
                            if(!SubscriberRecords.TryGetValue(e.RemoteEndPoint, out var record)) {
                                renewal = false;
                                record = SubscriberRecords[e.RemoteEndPoint] = new ClientRecord();
                            }

                            // Update subscribed-until
                            record.SubscribedUntil = DateTime.UtcNow.AddMilliseconds(ExpectedAnnounceInterval.TotalMilliseconds);

                            // If not a renewal, send all outsanding packets
                            if(!renewal) {
                                foreach(var packet in TopicRecords.Select(a => a.Value.Packet)) {
                                    Socket.SendTo(packet, e.RemoteEndPoint);
                                }
                            }
                        }

                        // Remove all expired subscriptions
                        foreach(var a in SubscriberRecords.Where(a => a.Value.SubscribedUntil < DateTime.UtcNow).Select(a => a.Key)) {
                            SubscriberRecords.TryRemove(a, out var record);
                        }
                    }

                    // Restart receiving
                    ReceiveAnnounce();
                });

                // Initiate receive
                Socket.ReceiveMessageFromAsync(socketEventArg);
            } catch(ObjectDisposedException) { }
        }

        public void Publish(UInt64 topic, Byte[] value) {
            // Handle un-publishes
            if(null == value) {
                TopicRecords.TryRemove(topic, out var a);
                return;
            }

            // TODO: Handle buffer sizes more smartly, and make them customisable?
            if(value.Length > ReceiveBufferSize - 10) {
                throw new ArgumentOutOfRangeException(nameof(value), $"Value must not exceed ReceiveBufferSize {ReceiveBufferSize}."); // TODO: Tidy
            }

            lock(Sync) {
                if(IsDisposed) {
                    throw new ObjectDisposedException("Object disposed.");
                }

                // Get topic, or create if needed
                if(TopicRecords.TryGetValue(topic, out var record)) {
                    record.Revision++;
                } else {
                    record = TopicRecords[topic] = new TopicRecord();
                }

                // Compose packet
                var packet = new Byte[value.Length + 10];
                Buffer.BlockCopy(BitConverter.GetBytes(topic), 0, packet, 0, 8);
                Buffer.BlockCopy(BitConverter.GetBytes(record.Revision), 0, packet, 8, 2);
                Buffer.BlockCopy(value, 0, packet, 10, value.Length);

                // Update record
                record.Packet = packet;
                record.LastSentAt = DateTime.MinValue;

                // Release lock
                SpinLock.Set();
            }
        }

        protected virtual void Dispose(Boolean disposing) {
            lock(Sync) {
                if(IsDisposed) {
                    return;
                }
                IsDisposed = true;

                if(disposing) {
                    // Dispose managed state (managed objects).
                    Socket?.Dispose();
                    PublishThread?.Join();
                }

                // Free unmanaged resources (unmanaged objects) and override a finalizer below.
                // Set large fields to null.
            }
        }


        public void Dispose() {
            Dispose(true);
        }
    }
}
