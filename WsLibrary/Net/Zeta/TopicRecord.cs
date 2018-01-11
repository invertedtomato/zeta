using System;

namespace InvertedTomato.Net.Zeta {
    public class TopicRecord {
        public UInt32 Channel { get; set; }
        public UInt32 Revision { get; set; }
        public ArraySegment<Byte> Packet { get; set; }
    }
}
