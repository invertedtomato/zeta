using System;
using System.Collections.Generic;
using System.Text;

namespace InvertedTomato.Net.Zeta {
    public class HandlerRecord {
        public UInt32 TopicLow;
        public UInt32 TopicHigh;
        public Delegate Handler;
        public Type MessageType;
    }
}
