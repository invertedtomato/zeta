using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace InvertedTomato.Zeta {
    public class ZetaClient : IDisposable {
        /// <summary>
        /// Server to communicate with
        /// </summary>
        private readonly IPEndPoint Server;

        /// <summary>
        /// Handler to pass received values to.
        /// </summary>
        private readonly Action<UInt64, UInt16, Byte[]> Handler;

        /// <summary>
        /// Underlying UDP socket.
        /// </summary>
        private readonly Socket Socket;

        /// <summary>
        /// Main thread
        /// </summary>
        private readonly Thread ReceiveThread;

        /// <summary>
        /// User-provided options
        /// </summary>
        private readonly Options Options;

        /// <summary>
        /// If the client is disposed and no longer operable.
        /// </summary>
        public Boolean IsDisposed { get; private set; }

        public ZetaClient(IPEndPoint server, Action<UInt64, UInt16, Byte[]> handler) : this(server, new Options(), handler) { }

        public ZetaClient(IPEndPoint server, Options options, Action<UInt64, UInt16, Byte[]> handler) {
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
            Socket.Bind(new IPEndPoint(IPAddress.Any, 0));

            // Start receiving
            ReceiveThread = new Thread(ReceieveThread_OnSpin);
            ReceiveThread.Start();
        }

        private void ReceieveThread_OnSpin(Object obj) {
            var lastAck = DateTime.MinValue;
            try {
                while(!IsDisposed) {
                    try {
                        // Receive packet
                        var endpoint = (EndPoint)Server;
                        var buffer = new Byte[Options.Mtu];
                        var len = Socket.ReceiveFrom(buffer, ref endpoint);
                        if(len < 1) {
                            Trace.TraceWarning($"CLIENT-RECEIVE: Strange byte count {len}.");
                            continue;
                        }

                        // Decode packet
                        var topic = BitConverter.ToUInt64(buffer, 0);
                        var revision = BitConverter.ToUInt16(buffer, 8);
                        var value = new Byte[len - 10];
                        Buffer.BlockCopy(buffer, 10, value, 0, value.Length);

                        // Send acknowledgement packet  FUTURE: batch ACK packets?
                        var packet = new Byte[Constants.CLIENTTXHEADER_LENGTH + 10];
                        packet[0] = Constants.VERSION;
                        Buffer.BlockCopy(Options.AuthorizationToken, 0, packet, 1, 16);
                        Buffer.BlockCopy(buffer, 0, packet, 17, 10);
                        Socket.SendTo(packet, Server);
                        lastAck = DateTime.UtcNow;

                        // Fire handler
                        Handler(topic, revision, value);
                    } catch(SocketException ex) {
                        // If nothing has been received recently...
                        if(ex.SocketErrorCode == SocketError.TimedOut) {
                            // If we are outside of the keep-alive window...
                            if((DateTime.UtcNow - lastAck) > Options.KeepAliveInterval) {
                                // Send keep-alive packet
                                var packet = new Byte[Constants.CLIENTTXHEADER_LENGTH];
                                packet[0] = Constants.VERSION;
                                Buffer.BlockCopy(Options.AuthorizationToken, 0, packet, 1, 16);
                                Socket.SendTo(packet, Server);
                                lastAck = DateTime.UtcNow;

                                Trace.TraceInformation($"CLIENT-RECEIVE: Keep-alive sent.");
                            }

                            // No need to report this
                            continue;
                        }
                        Trace.TraceWarning($"CLIENT-RECEIVE: Socket error occured. {ex.Message}");
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
                Socket?.Dispose();
            }

            // Free unmanaged resources (unmanaged objects) and override a finalizer below
            // Set large fields to null
        }

        public void Dispose() {
            Dispose(true);
        }
    }
}
