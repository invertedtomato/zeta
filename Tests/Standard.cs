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

            var server = new ZetaServer(1000);

            var client = new ZetaClient(new IPEndPoint(IPAddress.Loopback, 1000), (topic, revision, value) => {
                Assert.Equal((UInt64)0, topic);
                Assert.Equal(expected, revision);
                Assert.Equal(expected, value[0]);
                expected++;

            });

            Thread.Sleep(1000);

            server.Publish(new Byte[] { 0 });
            server.Publish(new Byte[] { 1 });
            server.Publish(new Byte[] { 2 });

            Thread.Sleep(1000);

            Assert.Equal((UInt32)3, expected);
        }
    }
}
