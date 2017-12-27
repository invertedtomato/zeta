using InvertedTomato.Net.Zeta;
using ProtoBuf;
using System;
using System.Net;
using System.Threading;
using Xunit;

namespace Tests {
    public class ProtoBuf {
        [Fact]
        public void FullRun() {
            UInt32 expected = 0;

            var server = new ZetaServer<Message>(1001);

            var client = new ZetaClient<Message>(new IPEndPoint(IPAddress.Loopback, 1001), (topic, revision, value) => {
                Assert.Equal((UInt64)1, topic);
                Assert.Equal(expected, revision);
                Assert.Equal(expected, value.Value);
                expected++;

            });

            Thread.Sleep(1000);

            server.Publish(new Message(0));
            server.Publish(new Message(1));
            server.Publish(new Message(2));

            Thread.Sleep(1000);

            Assert.Equal((UInt32)3, expected);
        }
    }

    [ProtoContract]
    public class Message {
        public Message() { }
        public Message(UInt32 value) { Value = value; }

        [ProtoMember(1)]
        public UInt32 Value { get; set; }
    }
}
