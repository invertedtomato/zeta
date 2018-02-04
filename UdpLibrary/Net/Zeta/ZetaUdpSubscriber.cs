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
    public class ZetaUdpSubscriber : IDisposable {
        /// <summary>
        /// Server to communicate with
        /// </summary>
        private EndPoint Server;

        /// <summary>
        /// Underlying UDP socket.
        /// </summary>
        private readonly Socket Socket;

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
        private readonly ConcurrentQueue<KeyValuePair<UInt32, UInt16>> AcknowledgementsPending = new ConcurrentQueue<KeyValuePair<UInt32, UInt16>>();

        private readonly Dictionary<UInt64, UInt16> TopicRevisions = new Dictionary<UInt64, UInt16>();

        private readonly Object Sync = new Object();

        /// <summary>
        /// The maximum packet size to send, and to expect to receive.
        /// </summary>
        public Int32 Mtu { get; set; } = 520;

        /// <summary>
        /// The maxium amount of received bytes to buffer before dropping packets.
        /// </summary>
        /// <remarks>
        /// The bigger this is the higher the potential latency. Current thinking is 2*Mtu is a good value
        /// </remarks>
        public Int32 ReceiveBufferSize { get { return Socket.ReceiveBufferSize; } set { Socket.ReceiveBufferSize = value; } }

        /// <summary>
        /// The maximum number of sending bytes to buffer before sending is halted.
        /// </summary>
        /// <remarks>
        /// The bigger this is the higher the potential latency. Current thinking is 2*Mtu is a good value
        /// </remarks>
        public Int32 SendBufferSize { get { return Socket.SendBufferSize; } set { Socket.SendBufferSize = value; } }

        /// <summary>
        /// The time before a read operation times out.
        /// </summary>
        /// <remarks>
        /// The default value is nil, which indicates an infinate time-out period.
        /// </remarks>
        public TimeSpan ReadTimeout { get { return new TimeSpan(0, 0, 0, 0, Socket.ReceiveTimeout); } set { Socket.ReceiveTimeout = (Int32)value.TotalMilliseconds; } }

        /// <summary>
        /// Time to live - the maximum number of router hops the packet can transverse before being dropped.
        /// </summary>
        /// <remarks>
        /// Only change this is you have a specific reason and you know what you're doing.
        /// </remarks>
        public Int16 Ttl { get { return Socket.Ttl; } set { Socket.Ttl = value; } }

        /// <summary>
        /// The client must send a packet to the server at least every interval.
        /// </summary>
        /// <remarks>
        /// Lower values allow detecting disconnected clients quicker, however consumes more data. Values musn't be too low however or (dump) NAT routers may consider the stream closed and start dropping packets. If your network uses NAT, I suggest 15 sec maximum!
        /// </remarks>
        public TimeSpan KeepAliveInterval { get; set; } = new TimeSpan(0, 0, 5);

        /// <summary>
        /// If the client is disposed and no longer operable.
        /// </summary>
        public Boolean IsDisposed { get; private set; }

        public Boolean IsStarted { get; private set; }

        /// <summary>
        /// Token for the client to send to identify itself. Must be 16 bytes.
        /// </summary>
        /// <remarks>
        /// Leave as all zeros if not required.
        /// </remarks>
        public Byte[] AuthorizationToken { get; set; } = new Byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

        public ZetaUdpSubscriber() {
            Socket = new Socket(SocketType.Dgram, ProtocolType.Udp) {
                SendTimeout = 1, // The lowest possible value - if we're suffering from buffer backpressure we want to retry with fresh data later anyway
                DualMode = true,
                ExclusiveAddressUse = true,
                SendBufferSize = 1040,
                ReceiveBufferSize = 1040
            };
            SendThread = new Thread(SendThread_OnSpin);
        }

        public void Start(String server) {
            Start(ParseIPEndPoint(server));
        }

        public void Start(EndPoint server) {
            if (null == server) {
                throw new ArgumentNullException(nameof(server));
            }
            if (16 != AuthorizationToken.Length) {
                throw new InvalidOperationException("AuthorizationToken must be exactly 16 bytes long.");
            }

            lock (Sync) {
                if (IsStarted) {
                    throw new InvalidOperationException("Already started.");
                }

                // Store params
                Server = server;

                // Bind socket
                Socket.Bind(new IPEndPoint(IPAddress.Any, 0));

                // Start sending
                SendThread.Start();

                IsStarted = true;
            }
        }



        /// <summary>
        /// Read a message with an expected type. Use this when you know what message type you're receiving.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public T Read<T>() where T : IImportableMessage, new() { return Read<T>(out var topic, out var revision); }
        public T Read<T>(out UInt32 topic, out UInt16 revision) where T : IImportableMessage, new() { // CCCCTTTTRRP*
            try {
                lock (Sync) {
                    if (!IsStarted) {
                        throw new InvalidOperationException("Not yet started.");
                    }

                    // Receive packet
                    var buffer = new Byte[Mtu];
                    var endpoint = Server;
                    var len = Socket.ReceiveFrom(buffer, ref endpoint);
                    Trace.WriteLineIf(len < 1, $"Strange byte count {len}.", "client-receive-warning");

                    // Read header
                    var typeCode = BitConverter.ToUInt32(buffer, 0);
                    topic = BitConverter.ToUInt32(buffer, 4);
                    revision = BitConverter.ToUInt16(buffer, 8);

                    // Get the head revision for the topic
                    if (!TopicRevisions.TryGetValue(topic, out var headRevision)) {
                        headRevision = UInt16.MaxValue;
                    }

                    // If the update isn't stale...
                    if (revision > headRevision || headRevision > UInt16.MaxValue / 4 * 3 && revision < UInt16.MaxValue / 4) {
                        // Note topic head
                        TopicRevisions[topic] = revision;

                        // Read payload
                        var payload = new ArraySegment<Byte>(buffer, 10, len - Constants.SERVERTXHEADER_LENGTH);

                        // Queue acknowledgement
                        AcknowledgementsPending.Enqueue(new KeyValuePair<UInt32, UInt16>(topic, revision));
                        SendLock.Set();

                        // Create message
                        var message = new T();

                        // Check typecode
                        if (message.TypeCode != typeCode) {
                            throw new MismatchTypeCode($"Read payload has typecode of {typeCode}, however trying to read to message with typecode of {message.TypeCode}.");
                        }

                        // Import payload into message
                        message.Import(payload);

                        // Return message
                        return message;
                    } else {
                        Trace.WriteLine($"Stale update {topic}#{revision} received (head is {headRevision}). Discarded.", "client-receive"); // This occurs when packets arrive out of order
                        return Read<T>(out topic, out revision); // Read again
                    }
                }
            } catch (SocketException ex) {
                if (ex.SocketErrorCode == SocketError.TimedOut || ex.SocketErrorCode == SocketError.Interrupted) {
                    throw new TimedOutException();
                }
                throw ex;
            }
        }

        /// <summary>
        /// Read a message when the type is unknown.
        /// </summary>
        /// <param name="register">Register of all possible message types, and a callback for each one.</param>
        public void Read(CallbackRegister register) { Read(register, out var topic, out var revision); }
        public void Read(CallbackRegister register, out UInt32 topic, out UInt16 revision) {
#if DEBUG
            if (null == register) {
                throw new ArgumentNullException(nameof(register));
            }
#endif
            lock (Sync) { // TODO: Is lock required here?
                if (!IsStarted) {
                    throw new InvalidOperationException("Not yet started.");
                }

                // Receive packet
                var buffer = new Byte[Mtu];
                var endpoint = Server;
                var len = Socket.ReceiveFrom(buffer, ref endpoint);
                Trace.WriteLineIf(len < 1, $"Strange byte count {len}.", "client-receive-warning");

                // Read header
                var typeCode = BitConverter.ToUInt32(buffer, 0);
                topic = BitConverter.ToUInt32(buffer, 4);
                revision = BitConverter.ToUInt16(buffer, 8);

                // Get the head revision for the topic
                if (!TopicRevisions.TryGetValue(topic, out var headRevision)) {
                    headRevision = UInt16.MaxValue;
                }

                // If the update isn't stale...
                if (revision > headRevision || headRevision > UInt16.MaxValue / 4 * 3 && revision < UInt16.MaxValue / 4) {
                    // Note topic head
                    TopicRevisions[topic] = revision;

                    // Read payload
                    var payload = new ArraySegment<Byte>(buffer, 10, len - Constants.SERVERTXHEADER_LENGTH);

                    // Queue acknowledgement
                    AcknowledgementsPending.Enqueue(new KeyValuePair<UInt32, UInt16>(topic, revision));
                    SendLock.Set();

                    // Invoke callback
                    register.Invoke(typeCode, payload);
                } else {
                    Trace.WriteLine($"Stale update {topic}#{revision} received (head is {headRevision}). Discarded.", "client-receive"); // This occurs when packets arrive out of order
                    Read(register, out topic, out revision); // Read again
                }
            }
        }

        private void SendThread_OnSpin(Object obj) {
            try {
                while (!IsDisposed) {
                    try {
                        // Wait for the next acknowledgement, or keep-alive interval - whichever is sooner
                        SendLock.WaitOne(KeepAliveInterval);

                        // Get pending acknowledgements, skipping duplicates
                        var pending = new Dictionary<UInt64, UInt16>();
                        while (AcknowledgementsPending.TryDequeue(out var item)) {
                            pending[item.Key] = item.Value; // There's no out-of-order issue here, as the updates were filtered as they arrived
                        }

                        // Compose header
                        var packet = new Byte[Constants.CLIENTTXHEADER_LENGTH + 10 * pending.Count];
                        packet[0] = Constants.VERSION;                                                  // UInt8 version
                        Buffer.BlockCopy(AuthorizationToken, 0, packet, 1, 16);                 // Byte[16] authorizationToken

                        // Attach acknowledgements
                        var pos = 17;
                        foreach (var item in pending) {
                            Buffer.BlockCopy(BitConverter.GetBytes(item.Key), 0, packet, pos, 8);       // UInt64 topic
                            Buffer.BlockCopy(BitConverter.GetBytes(item.Value), 0, packet, pos + 8, 2);   // UInt16 revision
                            pos += 10;
                        }

                        // Send packet
                        if (pending.Count == 0) {
                            Trace.WriteLine($"Sending keep-alive.", "client-send");
                        } else {
                            Trace.WriteLine($"Sending acknowledgements {string.Join(",", pending.Select(a => a.Key + "#" + a.Value))}.", "client-send");
                        }
                        var len = Socket.SendTo(packet, Server);
                        Trace.WriteLineIf(len != packet.Length, $"Strange byte count {len}.", "client-send-warning");
                    } catch (SocketException ex) {
                        // If nothing has been received recently...
                        if (ex.SocketErrorCode == SocketError.TimedOut) {
                            // No need to report this
                            continue;
                        }
                        Trace.WriteLine($"Socket error {ex.SocketErrorCode}.", "client-send-warning");
                    }
                }
            } catch (ObjectDisposedException) { }
        }


        protected virtual void Dispose(Boolean disposing) {
            if (IsDisposed) {
                return;
            }

            IsDisposed = true;

            if (disposing) {
                // Dispose managed state (managed objects)
                var sendThread = SendThread;
                if (sendThread != null && sendThread.IsAlive) {
                    sendThread.Join();
                }
                Socket?.Shutdown(SocketShutdown.Both);
                Socket?.Dispose();
            }

            // Free unmanaged resources (unmanaged objects) and override a finalizer below
            // Set large fields to null
        }

        public void Dispose() {
            Dispose(true);
        }

        private static IPEndPoint ParseIPEndPoint(String value) {
            if (null == value) {
                throw new ArgumentNullException(nameof(value));
            }

            var ep = value.Split(':');
            if (ep.Length < 2) {
                throw new FormatException("Invalid endpoint format");
            }

            IPAddress ip;
            if (ep.Length > 2) {
                if (!IPAddress.TryParse(string.Join(":", ep, 0, ep.Length - 1), out ip)) {
                    throw new FormatException("Invalid ip-adress");
                }
            } else {
                if (!IPAddress.TryParse(ep[0], out ip)) {
                    throw new FormatException("Invalid ip-adress");
                }
            }

            Int32 port;
            if (!int.TryParse(ep[ep.Length - 1], NumberStyles.None, NumberFormatInfo.CurrentInfo, out port)) {
                throw new FormatException("Invalid port");
            }

            return new IPEndPoint(ip, port);
        }
    }
}
