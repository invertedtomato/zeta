using InvertedTomato.IO.Messages;
using ProtoBuf;
using System;
using System.IO;
using System.Net;

namespace InvertedTomato.Net.Zeta {
    public class ZetaClientProtoBuf<T> : ZetaClient<BinaryMessage> {
        public ZetaClientProtoBuf(String server, Action<UInt64, UInt16, T> handler) : base(server, WrapHandler(handler)) { }

        public ZetaClientProtoBuf(EndPoint server, Action<UInt64, UInt16, T> handler) : base(server, WrapHandler(handler)) { }

        public ZetaClientProtoBuf(EndPoint server, Options options, Action<UInt64, UInt16, T> handler) : base(server, options, WrapHandler(handler)) { }

        private static Action<UInt64, UInt16, BinaryMessage> WrapHandler(Action<UInt64, UInt16, T> handler) {
            return (UInt64 topic, UInt16 revision, BinaryMessage payload) => {
                using(var stream = new MemoryStream(payload.Value)) {
                    var v = Serializer.Deserialize<T>(stream);
                    handler(topic, revision, v);
                }
            };
        }
    }
}
