using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Linq;
using System.Globalization;
using InvertedTomato.IO.Messages;

namespace InvertedTomato.Net.Zeta {
    public class ZetaClient<TMessage> : IDisposable where TMessage : IMessage, new() {
        /// <summary>
        /// Server to communicate with
        /// </summary>
        private readonly EndPoint Server;

        /// <summary>
        /// Handler to pass received values to.
        /// </summary>
        private readonly Action<UInt64, UInt16, TMessage> Handler;

        /// <summary>
        /// Underlying UDP socket.
        /// </summary>
        private readonly Socket Socket;

        /// <summary>
        /// Thread handing data receipt.
        /// </summary>
        private readonly Thread ReceiveThread;

        /// <summary>
        /// Thread handling data transmission.
        /// </summary>
        private readonly Thread SendThread;

        /// <summary>
        /// Lock preventing send thread busy-spinning.
        /// </summary>
        private readonly AutoResetEvent SendLock = new AutoResetEvent(true);

        /// <summary>
        /// Acknowledgements which are pending being sent back to the server
        /// </summary>
        private readonly ConcurrentQueue<KeyValuePair<UInt64, UInt16>> AcknowledgementsPending = new ConcurrentQueue<KeyValuePair<ulong, ushort>>();

        /// <summary>
        /// User-provided options
        /// </summary>
        private readonly Options Options;

        /// <summary>
        /// If the client is disposed and no longer operable.
        /// </summary>
        public Boolean IsDisposed { get; private set; }

        public ZetaClient(String server, Action<UInt64, UInt16, TMessage> handler) : this(ParseIPEndPoint(server), new Options(), handler) { }

        public ZetaClient(EndPoint server, Action<UInt64, UInt16, TMessage> handler) : this(server, new Options(), handler) { }

        public ZetaClient(EndPoint server, Options options, Action<UInt64, UInt16, TMessage> handler) {
            if(null == server) {
                throw new ArgumentNullException(nameof(server));
            }
            if(null == options) {
                throw new ArgumentNullException(nameof(options));
            }
            if(16 != options.AuthorizationToken.Length) {
                throw new ArgumentOutOfRangeException(nameof(options.AuthorizationToken), "Must be exactly 16 bytes long.");
            }
            if(null == handler) {
                throw new ArgumentNullException(nameof(handler));
            }

            // Store params
            Server = server;
            Options = options;
            Handler = handler;

            // Create and bind socket
            Socket = new Socket(SocketType.Dgram, ProtocolType.Udp); Socket.SendBufferSize = Options.SendBufferSize;
            Socket.ReceiveBufferSize = Options.ReceiveBufferSize;
            Socket.ReceiveTimeout = 1000; // Only so that we can actually land the plane neatly at shutdown time
            Socket.SendTimeout = 1; // The lowest possible value - if we're suffering from buffer backpressure we want to retry with fresh data later anyway
            Socket.DualMode = true;
            Socket.ExclusiveAddressUse = true;
            Socket.Ttl = options.Ttl;
            Socket.Bind(new IPEndPoint(IPAddress.Any, 0));

            // Start sending
            SendThread = new Thread(SendThread_OnSpin);
            SendThread.Start();

            // Start receiving
            ReceiveThread = new Thread(ReceieveThread_OnSpin);
            ReceiveThread.Start();
        }

        private void SendThread_OnSpin(Object obj) {
            try {
                while(!IsDisposed) {
                    try {
                        // Wait for the next acknowledgement, or keep-alive interval - whichever is sooner
                        SendLock.WaitOne(Options.KeepAliveInterval);

                        // Get pending acknowledgements, skipping duplicates
                        var pending = new Dictionary<UInt64, UInt16>();
                        while(AcknowledgementsPending.TryDequeue(out var item)) {
                            pending[item.Key] = item.Value; // There's no out-of-order issue here, as the updates were filtered as they arrived
                        }

                        // Compose header
                        var packet = new Byte[Constants.CLIENTTXHEADER_LENGTH + 10 * pending.Count];
                        packet[0] = Constants.VERSION;                                                  // UInt8 version
                        Buffer.BlockCopy(Options.AuthorizationToken, 0, packet, 1, 16);                 // Byte[16] authorizationToken

                        // Attach acknowledgements
                        var pos = 17;
                        foreach(var item in pending) {
                            Buffer.BlockCopy(BitConverter.GetBytes(item.Key), 0, packet, pos, 8);       // UInt64 topic
                            Buffer.BlockCopy(BitConverter.GetBytes(item.Value), 0, packet, pos + 8, 2);   // UInt16 revision
                            pos += 10;
                        }

                        // Send packet
                        if(pending.Count == 0) {
                            Trace.WriteLine($"Sending keep-alive.", "client-send");
                        } else {
                            Trace.WriteLine($"Sending acknowledgements {string.Join(",", pending.Select(a => a.Key + "#" + a.Value))}.", "client-send");
                        }
                        var len = Socket.SendTo(packet, Server);
                        Trace.WriteLineIf(len != packet.Length, $"Strange byte count {len}.", "client-send-warning");
                    } catch(SocketException ex) {
                        // If nothing has been received recently...
                        if(ex.SocketErrorCode == SocketError.TimedOut) {
                            // No need to report this
                            continue;
                        }
                        Trace.WriteLine($"Socket error {ex.SocketErrorCode}.", "client-send-warning");
                    }
                }
            } catch(ObjectDisposedException) { }
        }

        private void ReceieveThread_OnSpin(Object obj) {
            var lastAck = DateTime.MinValue;
            var topicRevisions = new Dictionary<UInt64, UInt16>();
            try {
                var buffer = new Byte[Options.Mtu];
                while(!IsDisposed) {
                    try {
                        // Receive packet
                        var endpoint = (EndPoint)Server;
                        var len = Socket.ReceiveFrom(buffer, ref endpoint);
                        Trace.WriteLineIf(len < 1, $"Strange byte count {len}.", "client-receive-warning");

                        // Decode header
                        var topic = BitConverter.ToUInt64(buffer, 0);
                        var revision = BitConverter.ToUInt16(buffer, 8);

                        // Get the head revision for the topic
                        if(!topicRevisions.TryGetValue(topic, out var headRevision)) {
                            headRevision = UInt16.MaxValue;
                        }

                        // If the update isn't stale...
                        if(revision > headRevision || headRevision > UInt16.MaxValue / 4 * 3 && revision < UInt16.MaxValue / 4) {
                            // Note topic head
                            topicRevisions[topic] = revision;

                            // Decode value
                            var payload = new Byte[len - Constants.SERVERTXHEADER_LENGTH];
                            Buffer.BlockCopy(buffer, 10, payload, 0, payload.Length);

                            // Queue acknowledgement
                            AcknowledgementsPending.Enqueue(new KeyValuePair<ulong, ushort>(topic, revision));
                            SendLock.Set();

                            // Load payload into message
                            var message = default(TMessage);
                            message.Import(payload);

                            // Fire handler
                            Handler(topic, revision, message);
                        } else {
                            Trace.WriteLine($"Stale update {topic}#{revision} received (head is {headRevision}). Discarded.", "client-receive"); // This occurs when packets arrive out of order
                        }
                    } catch(SocketException ex) {
                        // If nothing has been received recently...
                        if(ex.SocketErrorCode == SocketError.TimedOut) {
                            // No need to report this
                            continue;
                        }
                        Trace.WriteLine($"Socket error {ex.SocketErrorCode}.", "client-receive-warning");
                    }
                }
            } catch(ObjectDisposedException) { }
        }

        protected virtual void Dispose(Boolean disposing) {
            if(IsDisposed) {
                return;
            }

            IsDisposed = true;

            if(disposing) {
                // Dispose managed state (managed objects)
                ReceiveThread?.Join();
                SendThread?.Join();
                Socket?.Shutdown(SocketShutdown.Both);
                Socket?.Dispose();
            }

            // Free unmanaged resources (unmanaged objects) and override a finalizer below
            // Set large fields to null
        }

        public void Dispose() {
            Dispose(true);
        }

        private static IPEndPoint ParseIPEndPoint(string value) {
            if(null == value) {
                throw new ArgumentNullException(nameof(value));
            }

            var ep = value.Split(':');
            if(ep.Length < 2) {
                throw new FormatException("Invalid endpoint format");
            }

            IPAddress ip;
            if(ep.Length > 2) {
                if(!IPAddress.TryParse(string.Join(":", ep, 0, ep.Length - 1), out ip)) {
                    throw new FormatException("Invalid ip-adress");
                }
            } else {
                if(!IPAddress.TryParse(ep[0], out ip)) {
                    throw new FormatException("Invalid ip-adress");
                }
            }

            int port;
            if(!int.TryParse(ep[ep.Length - 1], NumberStyles.None, NumberFormatInfo.CurrentInfo, out port)) {
                throw new FormatException("Invalid port");
            }

            return new IPEndPoint(ip, port);
        }
    }
}
