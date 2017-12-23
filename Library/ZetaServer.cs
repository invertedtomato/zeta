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
        public TimeSpan RepublishInterval { get; set; } = new TimeSpan(0, 0, 30);

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
                    var subscriptionEndpoints = SubscriberRecords.Where(a => a.Value.SubscribedUntil >= DateTime.UtcNow).Select(a => a.Key);

                    // For each topic...
                    foreach(var item in TopicRecords) {
                        var record = item.Value;

                        // If the topic is due to be published...
                        if(record.At <= DateTime.UtcNow.Subtract(RepublishInterval)) {

                            // Send to each client..
                            foreach(var endpoint in subscriptionEndpoints) {
                                Socket.SendTo(record.Packet, endpoint);
                            }
                        }
                    }

                    // Wait until there is a new publish, or there is likely  for a republish to be required
                    SpinLock.WaitOne((int)(RepublishInterval.TotalMilliseconds / 2));
                }
            } catch(ObjectDisposedException) { }
        }

        private void ReceiveAnnounce() {
            try {
                var socketEventArg = new SocketAsyncEventArgs();
                //socketEventArg.RemoteEndPoint = Endpoint;
                socketEventArg.SetBuffer(new Byte[ReceiveBufferSize], 0, ReceiveBufferSize);
                socketEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(delegate (object s, SocketAsyncEventArgs e) {
                    if(IsDisposed) {
                        return;
                    }

                    if(e.SocketError != SocketError.Success) {
                        OnSocketError(e.SocketError);
                    } else {
                        // Extract authorization
                        var authorization = new Byte[e.Count];
                        Buffer.BlockCopy(e.Buffer, 0, authorization, 0, authorization.Length);

                        // Check subscriber is approved
                        if(SubscriberFilter(e.RemoteEndPoint, authorization)) {
                            // Create new record if needed
                            if(!SubscriberRecords.TryGetValue(e.RemoteEndPoint, out var record)) {
                                record = SubscriberRecords[e.RemoteEndPoint] = new ClientRecord();
                            }

                            // Update subscribed-until
                            record.SubscribedUntil = DateTime.UtcNow.AddMilliseconds(ExpectedAnnounceInterval.TotalMilliseconds);
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
                Socket.ReceiveAsync(socketEventArg);
            } catch(ObjectDisposedException) { }
        }
        // TODO: Police max length!!
        public void Publish(UInt64 topic, Byte[] value) {
            // Handle un-publishes
            if(null == value) {
                TopicRecords.TryRemove(topic, out var a);
                return;
            }

            lock(Sync) {
                if(IsDisposed) {
                    throw new ObjectDisposedException("Object disposed.");
                }
                
                // Get topic, or create if needed
                if(!TopicRecords.TryGetValue(topic, out var record)) {
                    record = TopicRecords[topic] = new TopicRecord();
                }

                // Compose packet
                var packet = new Byte[value.Length + 10];
                Buffer.BlockCopy(BitConverter.GetBytes(topic), 0, packet, 0, 8);
                Buffer.BlockCopy(BitConverter.GetBytes((UInt16)(record.Revision+1)), 0, packet, 10, 2);
                Buffer.BlockCopy(value, 0, packet, 10, value.Length);
                
                // Update record
                record.Packet = value;
                record.At = DateTime.UtcNow;
                record.Revision++;

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
