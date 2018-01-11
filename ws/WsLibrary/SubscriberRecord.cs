using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;

namespace InvertedTomato.WebPubSub {
    public class SubscriberRecord {
        public WebSocket Socket;
        public UInt32[] Channels;
        public AutoResetEvent SendLock = new AutoResetEvent(false);
    }
}
