using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net.WebSockets;
using System.Collections.Concurrent;

namespace InvertedTomato.WebPubSub {
    public class PubSubServer : IDisposable {
        private readonly String SubProtocol = "webpubsub";
        private readonly TimeSpan KeepAliveInterval = new TimeSpan(0, 0, 10);

        private readonly HttpListener Listener;
        private readonly Thread SendThread;
        private readonly Thread ReceiveThread;
        private readonly ConcurrentDictionary<UInt64, TopicRecord> Topics = new ConcurrentDictionary<UInt64, TopicRecord>();
        private readonly ConcurrentDictionary<UInt64, SubscriberRecord> Subscribers = new ConcurrentDictionary<UInt64, SubscriberRecord>();

        private Int64 NextSubscriberId = Int64.MinValue;

        //private readonly Object Sync = new object();

        public Boolean IsDisposed { get; private set; }

        public PubSubServer(String listenerPrefix) {
            if(null == listenerPrefix) {
                throw new ArgumentNullException(nameof(listenerPrefix));
            }

            // Create and start listener
            Listener = new HttpListener();
            Listener.Prefixes.Add(listenerPrefix);
            Listener.Start();

            // Create and start sending thread
            SendThread = new Thread(SendThread_OnSpin);
            SendThread.Start();

            // Create and start connection acceptance and receive thread
            ReceiveThread = new Thread(ReceiveThread_OnSpin) {
                Priority = ThreadPriority.Lowest
            };
            ReceiveThread.Start();
        }

        private async void ReceiveThread_OnSpin(Object obj) {
            try {
                // Loop until disposed
                while(!IsDisposed) {
                    // Wait for inbound request
                    var listenerContext = await Listener.GetContextAsync();

                    // Reject if not a websocket request
                    if(!listenerContext.Request.IsWebSocketRequest) {
                        listenerContext.Response.StatusCode = 426;
                        listenerContext.Response.StatusDescription = "WebSocket required";
                        listenerContext.Response.Close();
                        continue;
                    }

                    // TODO: authentication

                    // Upgrade to web sockets
                    WebSocketContext webSocketContext = null;
                    try {
                        webSocketContext = await listenerContext.AcceptWebSocketAsync(SubProtocol, KeepAliveInterval);
                    } catch(Exception e) {
                        listenerContext.Response.StatusCode = 400;
                        listenerContext.Response.StatusDescription = e.Message; // TODO: bad idea?
                        listenerContext.Response.Close();
                        return;
                    }

                    // TODO: determine groups

                    // Create subscriber record
                    var subscriberId = (UInt64)(Interlocked.Increment(ref NextSubscriberId) + Math.Abs(Int64.MinValue));
                    var subscriber = Subscribers[subscriberId] = new SubscriberRecord() {
                        Socket = webSocketContext.WebSocket
                    };

                    StartReceiving(subscriberId, subscriber);
                }
            } catch(ObjectDisposedException) { }
        }

        private async void StartReceiving(UInt64 subscriberId, SubscriberRecord subscriber) {
            var receiveBuffer = new Byte[512]; // TODO
            try {
                while(!IsDisposed && subscriber.Socket.State == WebSocketState.Open) {
                    // Receive message into buffer
                    var receiveResult = await subscriber.Socket.ReceiveAsync(new ArraySegment<Byte>(receiveBuffer), CancellationToken.None);

                    // Close connection if requested
                    if(receiveResult.MessageType == WebSocketMessageType.Close) {
                        await subscriber.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                        continue;
                    }

                    // Close connection on bad message type
                    if(receiveResult.MessageType == WebSocketMessageType.Text) {
                        await subscriber.Socket.CloseAsync(WebSocketCloseStatus.InvalidMessageType, "Cannot accept text frame", CancellationToken.None);
                        continue;
                    }

                    // Close connection if message too long
                    if(!receiveResult.EndOfMessage) {
                        await subscriber.Socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "Message too big", CancellationToken.None);
                        continue;
                    }

                    //await subscriber.Socket.SendAsync(new ArraySegment<Byte>(receiveBuffer, 0, receiveResult.Count), WebSocketMessageType.Binary, receiveResult.EndOfMessage, CancellationToken.None);
                }
            } catch {
            } finally {
                subscriber.Socket.Dispose();
                Subscribers.TryRemove(subscriberId, out subscriber);
            }
        }

        
        private void SendThread_OnSpin(Object obj) {
            try {
                while(!IsDisposed) {
                    foreach(var topicRecord in Topics) {

                    }
                    throw new NotImplementedException();
                }
            } catch(ObjectDisposedException) { }
        }

        public void Publish<TMessage>(TMessage message, UInt64 topic = 0, params String[] groups = new String["default"]) {
            throw new NotImplementedException();
        }


        protected virtual void Dispose(bool disposing) {
            if(!IsDisposed) {
                return;
            }
            IsDisposed = true;

            if(disposing) {
                // Dispose managed state (managed objects).
                Listener?.Close();
                SendThread?.Join();
                ReceiveThread?.Join();
            }

            // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
            // TODO: set large fields to null.
        }

        public void Dispose() {
            Dispose(true);
        }
    }

    public static class HttpListenerExtensions {
        public static Task<HttpListenerContext> GetContextAsync(this HttpListener listener) {
            return Task.Factory.FromAsync(listener.BeginGetContext, listener.EndGetContext, TaskCreationOptions.None);
        }
    }
}
