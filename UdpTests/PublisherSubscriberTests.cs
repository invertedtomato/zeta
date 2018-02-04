using InvertedTomato.IO.Messages;
using InvertedTomato.Net.Zeta;
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace UdpTests {
    public class PublisherSubscriberTests {
        [Fact]
        public void FullRun() {
            Byte expected = 0;

            // Start server
            var server = new ZetaUdpPublisher();
            server.Start(1000);

            // Start client
            var client = new ZetaUdpSubscriber();
            client.ReadTimeout = new TimeSpan(0, 0, 1);
            client.Start("127.0.0.1:1000");

            // Start reading
            var aborted = false;
            var readerThread = Task.Run(() => {
                try {
                    while (!client.IsDisposed) {
                        var msg = client.Read<BinaryMessage>(out var topic, out var revision);
                        Assert.Equal((UInt32)5, topic);
                        Assert.Equal(expected, revision);
                        Assert.Equal(expected, msg.Value[0]);
                        expected++;
                    }
                } catch (TimedOutException) {
                } finally {
                    aborted = true;
                }
            });


            Thread.Sleep(100);
            server.Publish(new BinaryMessage(new Byte[] { 0 }), 5);

            Thread.Sleep(100);
            server.Publish(new BinaryMessage(new Byte[] { 1 }), 5);

            Thread.Sleep(100);
            server.Publish(new BinaryMessage(new Byte[] { 2 }), 5);

            Thread.Sleep(100);
            Assert.Equal((Byte)3, expected);

            client.Dispose();
            Thread.Sleep(1000);
            Assert.True(aborted);
            server.Dispose();
        }
    }
}
