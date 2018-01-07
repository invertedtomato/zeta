using System;
using System.Collections.Generic;
using System.Text;

namespace InvertedTomato.WebPubSub {
    public class HandlerRecord {
        public Delegate Handler;
        public Type MessageType;
    }
}
