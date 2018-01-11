using InvertedTomato.IO.Messages;
using InvertedTomato.Net.Zeta;
using System;
using System.Net;
using System.Threading;
using Xunit;

namespace Tests {
    public class Standard {
        [Fact]
        public void FullRun() {
            UInt32 expected = 0;

            var server = new ZetaServer<BinaryMessage>(1000);

            var client = new ZetaClient<BinaryMessage>("127.0.0.1:1000", (topic, revision, payload) => {
                Assert.Equal((UInt64)0, topic);
                Assert.Equal(expected, revision);
                Assert.Equal(expected, payload.Value[0]);
                expected++;

            });

            Thread.Sleep(1000);

            server.Publish(new BinaryMessage(new Byte[] { 0 }));
            server.Publish(new BinaryMessage(new Byte[] { 1 }));
            server.Publish(new BinaryMessage(new Byte[] { 2 }));

            Thread.Sleep(1000);

            Assert.Equal((UInt32)3, expected);
        }
    }
}
