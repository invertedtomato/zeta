using System;
using System.Net;

namespace InvertedTomato.Zeta {
    public class TopicRecord {
        public EndPoint[] PendingSubscribers { get; set; }
        public DateTime SendAfter { get; set; }
        public UInt16 Revision { get; set; }
        public Byte[] Packet { get; set; }
    }
}