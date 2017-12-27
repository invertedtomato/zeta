using ProtoBuf;
using System;
using System.IO;
using System.Net;

namespace InvertedTomato.Net.Zeta {
    public class ZetaClient<T> : ZetaClient {
        public ZetaClient(EndPoint server, Action<UInt64, UInt16, T> handler) : base(server, Handle(handler)) { }

        public ZetaClient(EndPoint server, Options options, Action<UInt64, UInt16, T> handler) : base(server, options, Handle(handler)) { }

        private static Action<UInt64, UInt16, Byte[]> Handle(Action<UInt64, UInt16, T> handler) {
            return (UInt64 topic, UInt16 revision, Byte[] value) => {
                using(var stream = new MemoryStream(value)) {
                    var v = Serializer.Deserialize<T>(stream);
                    handler(topic, revision, v);
                }
            };
        }
    }
}
