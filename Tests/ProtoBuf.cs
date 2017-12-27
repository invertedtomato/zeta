using InvertedTomato.Zeta;
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

            var server = new ZetaServer<A>(1001);

            var client = new ZetaClient<A>(new IPEndPoint(IPAddress.Loopback, 1001), (topic, revision, value) => {
                Assert.Equal((UInt64)1, topic);
                Assert.Equal(expected, revision);
                Assert.Equal(expected, value.Value);
                expected++;

            });

            Thread.Sleep(1000);

            server.Publish(1, new A(0));
            server.Publish(1, new A(1));
            server.Publish(1, new A(2));

            Thread.Sleep(1000);

            Assert.Equal((UInt32)3, expected);
        }
    }

    [ProtoContract]
    public class A {
        public A() { }
        public A(UInt32 value) { Value = value; }

        [ProtoMember(1)]
        public UInt32 Value { get; set; }
    }
}
