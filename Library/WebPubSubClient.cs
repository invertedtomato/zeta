using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;

namespace InvertedTomato.WebPubSub {
    public class WebPubSubClient : IDisposable {
        private readonly ClientWebSocket Socket;

        public Boolean IsDisposed { get; private set; }

        public WebPubSubClient(String endpoint) : this(endpoint, new UInt64[] { 0 }, new Byte[] { }) { }

        public WebPubSubClient(String endpoint, UInt64[] channels, Byte[] authorization) {
            if(null == endpoint) {
                throw new ArgumentNullException(nameof(endpoint));
            }
            if(null == channels) {
                throw new ArgumentNullException(nameof(channels));
            }
            if(null == authorization) {
                throw new ArgumentNullException(nameof(authorization));
            }

            // Connect to server
            var target = $"{endpoint}?channels={String.Join(",", channels)}&authorization={BitConverter.ToString(authorization)}";
            Socket = new ClientWebSocket();
            Socket.ConnectAsync(new Uri(target), CancellationToken.None).RunSynchronously();

            StartReceive();
        }

        private async void StartReceive() {
            try {
                while(!IsDisposed) {
                    //await Socket.ReceiveAsync();

                    // TODO
                }
            } catch(ObjectDisposedException) {
            } finally {
                Socket.Dispose();
            }
        }

        public void Subscribe<TMessage>(Action<UInt64, UInt64, TMessage> handler) {
            Subscribe(handler, UInt64.MinValue, UInt64.MaxValue);
        }
        public void Subscribe<TMessage>(Action<UInt64, UInt64, TMessage> handler, UInt64 topicLow = 0, UInt64 topicHigh = 0) {
            throw new NotImplementedException();
        }


        protected virtual void Dispose(Boolean disposing) {
            if(IsDisposed) {
                return;
            }
            IsDisposed = true;

            if(disposing) {
                // TODO: dispose managed state (managed objects).
            }

            // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
            // TODO: set large fields to null.
        }

        public void Dispose() {
            Dispose(true);
        }
    }
}
