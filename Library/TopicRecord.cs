using System;

namespace InvertedTomato.Zeta {
    public class TopicRecord {
        public DateTime At { get; set; }
        public UInt16 Revision { get; set; }
        public Byte[] Packet { get; set; }
    }
}