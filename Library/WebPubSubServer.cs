using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net.WebSockets;
using System.Collections.Concurrent;
using InvertedTomato.IO.Messages;
using System.Diagnostics;
using System.Reflection;
using System.IO;

namespace InvertedTomato.WebPubSub {
    public class WebPubSubServer : IDisposable {
        private readonly String SubProtocol = "webpubsub";
        private readonly TimeSpan KeepAliveInterval = new TimeSpan(0, 0, 10);

        private readonly HttpListener Listener;
        private readonly Thread AcceptThread;
        private readonly ConcurrentDictionary<UInt32, TopicRecord> TopicRecords = new ConcurrentDictionary<UInt32, TopicRecord>();
        private readonly ConcurrentDictionary<UInt64, SubscriberRecord> SubscriberRecords = new ConcurrentDictionary<UInt64, SubscriberRecord>();
        private readonly Object Sync = new Object();
        private readonly Byte[] ClientTypeScript;
        private readonly Byte[] ClientJavaScript;

        private Int64 NextSubscriberId = Int64.MinValue;


        public Boolean IsDisposed { get; private set; }

        public WebPubSubServer(String listenerPrefix) {
            if(null == listenerPrefix) {
                throw new ArgumentNullException(nameof(listenerPrefix));
            }

            // Load web client libraries
            var path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            ClientTypeScript = File.ReadAllBytes($"{path}/WebPubSubClient.ts");
            ClientJavaScript = File.ReadAllBytes($"{path}/WebPubSubClient.js");

            // Create and start listener
            Listener = new HttpListener();
            Listener.Prefixes.Add(listenerPrefix);
            Listener.Start();

            // Create and start connection acceptance and receive thread
            AcceptThread = new Thread(AcceptThread_OnSpin) {
                Priority = ThreadPriority.BelowNormal
            };
            AcceptThread.Start();
        }

        private async void AcceptThread_OnSpin(Object obj) {
            try {
                // Loop until disposed
                while(!IsDisposed) {
                    // Wait for inbound request
                    var listenerContext = await Listener.GetContextAsync();

                    // Respond to options requests
                    if(listenerContext.Request.HttpMethod == "OPTIONS") {
                        listenerContext.Response.AddHeader("Access-Control-Allow-Headers", "Content-Type, Accept, X-Requested-With");
                        listenerContext.Response.AddHeader("Access-Control-Allow-Methods", "GET");
                        listenerContext.Response.AddHeader("Access-Control-Max-Age", "86400");
                        listenerContext.Response.Close();
                        continue;
                    }
                    listenerContext.Response.AppendHeader("Access-Control-Allow-Origin", "*");

                    // Handle requests for client
                    if(listenerContext.Request.Url.PathAndQuery == "/client.ts") {
                        listenerContext.Response.StatusCode = 200;
                        listenerContext.Response.StatusDescription = "OK";
                        listenerContext.Response.ContentType = "application/typescript";
                        listenerContext.Response.Close(ClientTypeScript, false);
                        continue;
                    }
                    if(listenerContext.Request.Url.PathAndQuery == "/client.js") {
                        listenerContext.Response.StatusCode = 200;
                        listenerContext.Response.StatusDescription = "OK";
                        listenerContext.Response.ContentType = "application/javascript";
                        listenerContext.Response.Close(ClientJavaScript, false);
                        continue;
                    }

                    // Favicon
                    if(listenerContext.Request.Url.PathAndQuery == "/favicon.ico") {
                        listenerContext.Response.StatusCode = 404;
                        listenerContext.Response.StatusDescription = "Not found";
                        listenerContext.Response.Close(ClientJavaScript, false);
                        continue;
                    }

                    // Reject if not a websocket request
                    if(!listenerContext.Request.IsWebSocketRequest) {
                        listenerContext.Response.StatusCode = 426;
                        listenerContext.Response.StatusDescription = "WebSocket required";
                        listenerContext.Response.Close();
                        continue;
                    }

                    // Get channel list
                    var channelsStrings = listenerContext.Request.QueryString["channels"];
                    if(String.IsNullOrWhiteSpace(channelsStrings)) {
                        listenerContext.Response.StatusCode = 400;
                        listenerContext.Response.StatusDescription = "Missing channels";
                        listenerContext.Response.Close();
                        continue;
                    }
                    var channels = new List<UInt32>();
                    foreach(var channelString in channelsStrings.Split(',')) {
                        if(!UInt32.TryParse(channelString, out var channel)) {
                            listenerContext.Response.StatusCode = 400;
                            listenerContext.Response.StatusDescription = $"Bad channel {channelString}";
                            listenerContext.Response.Close();
                            continue;
                        }
                        channels.Add(channel);
                    }

                    // TODO: reject if not secure?
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

                    // Create subscriber record
                    var subscriberId = (UInt64)(Interlocked.Increment(ref NextSubscriberId) - Int64.MinValue);
                    var subscriber = SubscriberRecords[subscriberId] = new SubscriberRecord() {
                        Socket = webSocketContext.WebSocket,
                        Channels = channels.ToArray()
                    };

                    Task.Run(async () => StartReceiving(subscriberId, subscriber)); // TODO: Clean this up
                    Task.Run(async () => StartSending(subscriberId, subscriber));
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
            } catch(ObjectDisposedException) { // TODO: other exceptions
            } catch(Exception e) {
                Trace.TraceWarning($"Subscriber {subscriberId} RX: {e.Message}");
            } finally {
                subscriber.Socket.Dispose();
                SubscriberRecords.TryRemove(subscriberId, out subscriber);
            }
        }


        private async void StartSending(UInt64 subscriberId, SubscriberRecord subscriber) {
            try {
                var lastRevisions = new Dictionary<UInt32, UInt32>(); // {topic} => {revision}

                // Loop until disposed or closed
                while(!IsDisposed && subscriber.Socket.State == WebSocketState.Open) {
                    foreach(var topicRecord in TopicRecords) {
                        // If the client is in need of an update
                        if(subscriber.Channels.Contains(topicRecord.Value.Channel) && // Correct channel
                            (!lastRevisions.TryGetValue(topicRecord.Key, out var lastRevision) || lastRevision < topicRecord.Value.Revision)) // There's a new revision
                            {
                            // Send update
                            await subscriber.Socket.SendAsync(topicRecord.Value.Packet, WebSocketMessageType.Binary, true, CancellationToken.None);

                            // Note latest revision sent
                            lastRevisions[topicRecord.Key] = topicRecord.Value.Revision;
                        }
                    }

                    // Wait for next change
                    subscriber.SendLock.WaitOne();
                }
            } catch(ObjectDisposedException) {
            } catch(Exception e) {
                Trace.TraceWarning($"Subscriber {subscriberId} TX: {e.Message}");
            }
        }

        public void Publish<TMessage>(TMessage message, UInt32 topic = 0, UInt32 channel = 0) where TMessage : IMessage {
            // Handle un-publishes
            if(null == message) {
                TopicRecords.TryRemove(topic, out var a);
                return;
            }

            // Extract payload
            var payload = message.Export();

            lock(Sync) {
                if(IsDisposed) {
                    throw new ObjectDisposedException("Object disposed.");
                }

                // Create new topic record
                var record = new TopicRecord() {
                    Channel = channel,
                    Revision = 0
                };

                // Calculate new revision
                if(TopicRecords.TryGetValue(topic, out var lastRecord)) {
                    record.Revision = lastRecord.Revision + 1;
                }

                // Compose packet
                var packet = new Byte[Constants.SERVERTXHEADER_LENGTH + payload.Count];
                Buffer.BlockCopy(BitConverter.GetBytes(topic), 0, packet, 0, 4);             // UInt32 topic
                Buffer.BlockCopy(BitConverter.GetBytes(record.Revision), 0, packet, 4, 4);   // UInt32 revision
                Buffer.BlockCopy(payload.Array, payload.Offset, packet, 8, payload.Count);  // Byte[?] value
                record.Packet = new ArraySegment<Byte>(packet);

                // Release topic update
                TopicRecords[topic] = record;
                foreach(var subscriber in SubscriberRecords.Values) {
                    if(subscriber.Channels.Contains(channel)) { // Over optimisation?
                        subscriber.SendLock.Set();
                    }
                }
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
                    Listener?.Close();
                    AcceptThread?.Join();
                }

                // Set large fields to null
            }
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
