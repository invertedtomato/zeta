using System;
using System.Collections.Generic;
using System.Text;

namespace InvertedTomato.WebPubSub {
    public class TopicRecord {
        public UInt64 Channel { get; set; }
        public UInt64 Revision { get; set; }
        public ArraySegment<Byte> Packet { get; set; }
    }
}
