﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Linq;
using InvertedTomato.IO.Messages;

namespace InvertedTomato.WebPubSub {
    public class WebPubSubClient : IDisposable {
        private readonly String SubProtocol = "webpubsub";
        private readonly TimeSpan KeepAliveInterval = new TimeSpan(0, 0, 10);
        private readonly Int32 MaxMessageSize = 64 * 1024;

        private readonly ClientWebSocket Socket;

        private readonly Object Sync = new Object();
        private readonly List<HandlerRecord> Handlers = new List<HandlerRecord>();

        public Boolean IsDisposed { get; private set; }

        public WebPubSubClient(String endpoint) : this(endpoint, new UInt64[] { 0 }, new Byte[] { }) { }
        public WebPubSubClient(String endpoint, UInt64[] channels) : this(endpoint, channels, new Byte[] { }) { }
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
            var buffer = new Byte[MaxMessageSize];
            try {
                while(!IsDisposed && Socket.State == WebSocketState.Open) {
                    // Read result
                    var result = await Socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                    // Close connection if requested
                    if(result.MessageType == WebSocketMessageType.Close) {
                        await Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                        continue;
                    }

                    // Close connection on bad message type
                    if(result.MessageType == WebSocketMessageType.Text) {
                        await Socket.CloseAsync(WebSocketCloseStatus.InvalidMessageType, "Cannot accept text frame", CancellationToken.None);
                        continue;
                    }

                    // Handle messages that are too large
                    if(!result.EndOfMessage) { // TODO: increase buffer size instead
                        await Socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, string.Empty, CancellationToken.None);
                        continue;
                    }

                    // Check packet sanity
                    if(result.Count < 16) {
                        await Socket.CloseAsync(WebSocketCloseStatus.InvalidPayloadData, "Message not long enough to contain header", CancellationToken.None);
                        continue;
                    }

                    // Parse
                    var topic = BitConverter.ToUInt64(buffer, 0);
                    var revision = BitConverter.ToUInt64(buffer, 8);

                    // Get handler
                    HandlerRecord selectedHandler = null;
                    lock(Sync) {
                        foreach(var handler in Handlers) {
                            if(handler.TopicLow <= topic && handler.TopicHigh >= topic) {
                                selectedHandler = handler;
                                break;
                            }
                        }
                    }
                    if(null == selectedHandler) {
                        Trace.TraceWarning($"RX: Unexpected topic {topic}#{revision}");
                        continue;
                    }

                    // Create message
                    var message = (IMessage)Activator.CreateInstance(selectedHandler.MessageType);
                    message.Import(new ArraySegment<Byte>(buffer, 16, result.Count - 16));

                    // Raise handler
                    selectedHandler.Handler.DynamicInvoke(topic, revision, message);
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
        public void Subscribe<TMessage>(Action<UInt64, UInt64, TMessage> handler, UInt64 topic) {
            Subscribe(handler, topic, topic);
        }
        public void Subscribe<TMessage>(Action<UInt64, UInt64, TMessage> handler, UInt64 topicLow, UInt64 topicHigh) {
            if(null == handler) {
                throw new ArgumentNullException(nameof(handler));
            }
            if(topicHigh < topicLow) {
                throw new ArgumentOutOfRangeException(nameof(topicHigh));
            }

            lock(Sync) {
                if(IsDisposed) {
                    throw new ObjectDisposedException(string.Empty);
                }

                if(Handlers.Any(a => (a.TopicLow >= topicLow && a.TopicLow <= topicHigh) || (a.TopicHigh <= topicLow && a.TopicHigh >= topicHigh))) {
                    throw new InvalidOperationException("There is already a handler covering this range, or part of this range of topics.");
                }

                // Add handler
                Handlers.Add(new HandlerRecord() {
                    TopicHigh = topicHigh,
                    TopicLow = topicLow,
                    Handler = handler,
                    MessageType = typeof(TMessage)
                });
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
                }

                // Set large fields to null.
            }
        }

        public void Dispose() {
            Dispose(true);
        }
    }
}
