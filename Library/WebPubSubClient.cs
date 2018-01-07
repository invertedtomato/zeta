using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Linq;

namespace InvertedTomato.WebPubSub {
    public class WebPubSubClient : IDisposable {
        private readonly String SubProtocol = "webpubsub";
        private readonly TimeSpan KeepAliveInterval = new TimeSpan(0, 0, 10);
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
            if(!endpoint.EndsWith("/")) {
                throw new ArgumentException("Must end with a '/'.", nameof(endpoint));
            }
            if(!endpoint.StartsWith("ws://") && !endpoint.StartsWith("wss://")) {
                throw new ArgumentException("Must start with a 'ws://' or 'wss://'.", nameof(endpoint));
            }

            // Connect to server
            var target = $"{endpoint}?channels={String.Join(",", channels)}&authorization={BitConverter.ToString(authorization)}";
            Socket = new ClientWebSocket();
            Socket.Options.AddSubProtocol(SubProtocol);
            Socket.Options.KeepAliveInterval = KeepAliveInterval;
            Socket.ConnectAsync(new Uri(target), CancellationToken.None).Wait();

            StartReceive();
        }

        private async void StartReceive() {
            var buffer = new Byte[512]; // TODO
            try {
                while(!IsDisposed && Socket.State == WebSocketState.Open) {
                    // Read result
                    var result = await Socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                    // Close connection if requested
                    if(result.MessageType == WebSocketMessageType.Close) {
                        await Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                        continue;
                    }

                    // Close connection on bad message type
                    if(result.MessageType == WebSocketMessageType.Text) {
                        await Socket.CloseAsync(WebSocketCloseStatus.InvalidMessageType, "Cannot accept text frame", CancellationToken.None);
                        continue;
                    }

                    // Report message
                    Trace.TraceWarning($"RX: Unexpected message {BitConverter.ToString(buffer.Take(result.Count).ToArray())}");
                }
            } catch(ObjectDisposedException) {
            } catch(Exception e) {
                Trace.TraceWarning($"RX: {e.Message}");
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
