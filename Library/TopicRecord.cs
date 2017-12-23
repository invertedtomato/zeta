using System;

namespace InvertedTomato.Zeta {
    public class TopicRecord {
        public DateTime LastSentAt { get; set; }
        public UInt16 Revision { get; set; }
        public Byte[] Packet { get; set; }
    }
}