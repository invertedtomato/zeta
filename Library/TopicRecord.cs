using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;

namespace InvertedTomato.Zeta {
    public class TopicRecord {
        public ConcurrentDictionary<EndPoint, EndPoint> PendingSubscribers { get; set; }
        public DateTime SendAfter { get; set; }
        public UInt16 Revision { get; set; }
        public Byte[] Packet { get; set; }
    }
}