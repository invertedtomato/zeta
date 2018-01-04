using System;
using System.Collections.Generic;
using System.Text;

namespace InvertedTomato.WebPubSub {
    public class PubSubClient
    {
        public Client(EndPoint, Options); // Groups in options
        public void Subscribe<TMessage>(UInt64 topicLow = 0, topicHigh= 0, Action<UInt64 topic, UInt16 revision, TMessage message);
        public void Dispose();
    }
}
