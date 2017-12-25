using System;
using System.Net;

namespace InvertedTomato.Zeta {
    public class Options {
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
        public Int32 ReceiveBufferSize { get; set; } = 1040;

        /// <summary>
        /// The maximum number of sending bytes to buffer before sending is halted.
        /// </summary>
        /// <remarks>
        /// The bigger this is the higher the potential latency. Current thinking is 2*Mtu is a good value
        /// </remarks>
        public Int32 SendBufferSize { get; set; } = 1500;

        /// <summary>
        /// Token for the client to send to identify itself. Must be 16 bytes.
        /// </summary>
        /// <remarks>
        /// Leave as all zeros if not required.
        /// </remarks>
        public Byte[] AuthorizationToken { get; set; } = new Byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

        /// <summary>
        /// Method for server to accept or reject client subscription requests.
        /// </summary>
        /// <remarks>
        /// This is called very often, so use caching if required.
        /// </remarks>
        public Func<EndPoint, Byte[], Boolean> AuthorizationFilter { get; set; } = (endpoint, token) => { return true; };

        /// <summary>
        /// The client must send a packet to the server at least every interval.
        /// </summary>
        /// <remarks>
        /// Lower values allow detecting disconnected clients quicker, however consumes more data. Values musn't be too low however or (dump) NAT routers may consider the stream closed and start dropping packets. If your network uses NAT, I suggest 15 sec maximum!
        /// </remarks>
        public TimeSpan KeepAliveInterval { get; set; } = new TimeSpan(0, 0, 5);

        /// <summary>
        /// The amount of time past the keep-alive interval for the server to keep sending data to a client. The client will be disconnected when both the KeepAliveInterval and KeepAliveGrace have elapsed.
        /// </summary>
        /// <remarks>
        /// This is to stop clients getting disconnected when there is packet loss on the network. Current thinking is that it should be 2*KeepAliveInterval.
        /// </remarks>
        public TimeSpan KeepAiveGrace { get; set; } = new TimeSpan(0, 0, 0, 10);

        /// <summary>
        /// If a subscriber hasn't acnowledged recept of a value, retransmit it after this period.
        /// </summary>
        /// <remarks>
        /// This should be at least the round-trip-time on the network.
        /// </remarks>
        public TimeSpan RetransmitInterval { get; set; } = new TimeSpan(0, 0, 0, 0, 250);
    }
}
