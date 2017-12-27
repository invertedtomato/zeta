using ProtoBuf;
using System;
using System.IO;
using System.Net;

namespace InvertedTomato.Net.Zeta {
    public class ZetaServer<T> : ZetaServer {
        public ZetaServer(UInt16 port) : base(port) { }

        public ZetaServer(EndPoint endpoint) : base(endpoint) { }

        public ZetaServer(EndPoint endpoint, Options options) : base(endpoint, options) { }

        public void Publish(T value) {
            Publish(0, value);
        }

        public void Publish(UInt64 topic, T value) {
            using(var stream = new MemoryStream()) {
                Serializer.Serialize(stream, value);
                Publish(topic, stream.ToArray());
            }
        }
    }
}
