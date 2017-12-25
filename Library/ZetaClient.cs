using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace InvertedTomato.Zeta {
    public class ZetaClient : IDisposable {
        private readonly IPEndPoint Endpoint;
        private readonly Action<UInt64, UInt16, Byte[]> Handler;
        private readonly Socket Socket;
        private readonly Thread ReceiveThread;

        private readonly Options Options;

        public Boolean IsDisposed { get; private set; }

        public ZetaClient(IPEndPoint endpoint, Action<UInt64, UInt16, Byte[]> handler) : this(endpoint, new Options(), handler) { }

        public ZetaClient(IPEndPoint endpoint, Options options, Action<UInt64, UInt16, Byte[]> handler) {
            if(null == endpoint) {
                throw new ArgumentNullException(nameof(endpoint));
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
            Endpoint = endpoint;
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
                        var endpoint = (EndPoint)Endpoint;
                        var buffer = new Byte[1500];
                        var len = Socket.ReceiveFrom(buffer, ref endpoint);
                        if(len < 1) {
                            Trace.TraceWarning($"CLIENT-RECEIVE: Yielded {len}.");
                            continue;
                        }

                        // Read packet
                        var topicId = BitConverter.ToUInt64(buffer, 0);
                        var revision = BitConverter.ToUInt16(buffer, 8);
                        var value = new Byte[len - 10];
                        Buffer.BlockCopy(buffer, 10, value, 0, value.Length);

                        // ACK packet // TODO: possible to batch ACKs
                        var packet = new Byte[Constants.CLIENTTXHEADER_LENGTH + 10];
                        packet[0] = Constants.VERSION;
                        Buffer.BlockCopy(Options.AuthorizationToken, 0, packet, 1, 16);
                        Buffer.BlockCopy(buffer, 0, packet, 17, 10);
                        Socket.SendTo(packet, Endpoint);
                        lastAck = DateTime.UtcNow;

                        // Fire handler
                        Handler(topicId, revision, value);
                    } catch(SocketException ex) {
                        if(ex.SocketErrorCode == SocketError.TimedOut) {
                            if((DateTime.UtcNow - lastAck) > Options.KeepAliveInterval) {
                                var b = new Byte[Constants.CLIENTTXHEADER_LENGTH];
                                b[0] = Constants.VERSION;
                                Buffer.BlockCopy(Options.AuthorizationToken, 0, b, 1, 16);
                                Socket.SendTo(b, Endpoint);
                                lastAck = DateTime.UtcNow;
                            }
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
