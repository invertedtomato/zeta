using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace InvertedTomato.Zeta {
    public class ZetaClient : IDisposable {
        private readonly IPEndPoint Endpoint;
        private readonly Byte[] Authorization;
        private readonly Action<UInt64, UInt16, Byte[]> Handler;
        private readonly Socket Socket;
        private readonly Timer AnnounceTimer;

        public Boolean IsDisposed { get; private set; }
        public event Action<SocketError> OnSocketError;

        private static readonly TimeSpan AnnounceInterval = new TimeSpan(0, 0, 5);
        private static readonly UInt16 ReceiveBufferSize = 1500;

        public ZetaClient(IPEndPoint endpoint, Action<UInt64, UInt16, Byte[]> handler) : this(endpoint, new Byte[] { }, handler) { }

        public ZetaClient(IPEndPoint endpoint, Byte[] authorization, Action<UInt64, UInt16, Byte[]> handler) {
            if(null == endpoint) {
                throw new ArgumentNullException(nameof(endpoint));
            }
            if(null == authorization) {
                throw new ArgumentNullException(nameof(authorization));
            }
            if(null == handler) {
                throw new ArgumentNullException(nameof(handler));
            }

            // Store params
            Endpoint = endpoint;
            Authorization = authorization;
            Handler = handler;

            // Create and bind socket
            Socket = new Socket(SocketType.Dgram, ProtocolType.Udp);
            Socket.Bind(new IPEndPoint(IPAddress.Any, 0));

            // Seed receiving
            ReceiveValue(); // If receive performance isn't good enough we could call this multiple times to inrease the receive-pool size

            // Setup announce timer
            AnnounceTimer = new Timer(AnnounceTimer_OnTick, null, TimeSpan.Zero, AnnounceInterval);
        }

        private void AnnounceTimer_OnTick(object o) {
            // Send token to keep the connection alive
            Socket.SendTo(Authorization, Endpoint);
        }

        private void ReceiveValue() {
            try {
                var socketEventArg = new SocketAsyncEventArgs();
                socketEventArg.RemoteEndPoint = Endpoint;
                socketEventArg.SetBuffer(new Byte[ReceiveBufferSize], 0, ReceiveBufferSize);
                socketEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(delegate (object s, SocketAsyncEventArgs e) {
                    if(IsDisposed) {
                        return;
                    }

                    if(e.SocketError != SocketError.Success) {
                        OnSocketError(e.SocketError);
                    } else {
                        var topic = BitConverter.ToUInt64(e.Buffer, 0);
                        var revision = BitConverter.ToUInt16(e.Buffer, 8);
                        var value = new Byte[e.Count - 10];
                        Buffer.BlockCopy(e.Buffer, 10, value, 0, value.Length);

                        Handler(topic, revision, value);
                    }

                    // Restart receiving
                    ReceiveValue();
                });

                // Initiate receive
                Socket.ReceiveFromAsync(socketEventArg);
            } catch(ObjectDisposedException) { }
        }

        protected virtual void Dispose(bool disposing) {
            if(IsDisposed) {
                return;
            }

            IsDisposed = true;

            if(disposing) {
                // Dispose managed state (managed objects).
                AnnounceTimer?.Dispose();
                Socket?.Dispose();
            }

            // Free unmanaged resources (unmanaged objects) and override a finalizer below.
            // Set large fields to null.

        }

        public void Dispose() {
            Dispose(true);
        }
    }
}
