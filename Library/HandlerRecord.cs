using System;
using System.Collections.Generic;
using System.Text;

namespace InvertedTomato.WebPubSub {
    public class HandlerRecord {
        public UInt64 TopicLow;
        public UInt64 TopicHigh;
        public Delegate Handler;
        public Type MessageType;
    }
}
