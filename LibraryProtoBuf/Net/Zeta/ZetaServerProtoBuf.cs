using InvertedTomato.IO.Messages;
using ProtoBuf;
using System;
using System.IO;
using System.Net;

namespace InvertedTomato.Net.Zeta {
    public class ZetaServerProtoBuf<T> : ZetaServer<BinaryMessage> {
        public ZetaServerProtoBuf(UInt16 port) : base(port) { }

        public ZetaServerProtoBuf(EndPoint endpoint) : base(endpoint) { }

        public ZetaServerProtoBuf(EndPoint endpoint, Options options) : base(endpoint, options) { }

        public void Publish(T value) {
            Publish(0, value);
        }

        public void Publish(UInt64 topic, T payload) {
            using(var stream = new MemoryStream()) {
                Serializer.Serialize(stream, payload);
                Publish(topic, new BinaryMessage(stream.ToArray()));
            }
        }
    }
}
