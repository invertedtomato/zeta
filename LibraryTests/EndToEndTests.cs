using InvertedTomato.IO.Messages;
using InvertedTomato.WebPubSub;
using System;
using System.Collections.Generic;
using System.Threading;
using Xunit;

namespace LibraryTests {
    public class EndToEndTests {
        [Fact]
        public void EndToEnd_AfterConnection() {
            var revisions = new Dictionary<UInt64, UInt64>();
            var values = new Dictionary<UInt64, String>();

            var server = new WebPubSubServer("http://+:8000/");
            var client = new WebPubSubClient("ws://localhost:8000/");
            client.Subscribe((UInt64 topic, UInt64 revision, StringMessage message) => {
                revisions[topic] = revision;
                values[topic] = message.Value;
            });

            Thread.Sleep(100);

            server.Publish(new StringMessage("Topic 0, message 1"), 0);
            server.Publish(new StringMessage("Topic 1, message 1"), 1);
            server.Publish(new StringMessage("Topic 1, message 2"), 1);
            server.Publish(new StringMessage("Topic 2, message 1"), 2);

            Thread.Sleep(100);
            Assert.Equal("Topic 0, message 1", values[0]);
            Assert.Equal("Topic 1, message 2", values[1]);
            Assert.Equal("Topic 2, message 1", values[2]);
            Assert.Equal((UInt64)0, revisions[0]);
            Assert.Equal((UInt64)1, revisions[1]);
            Assert.Equal((UInt64)0, revisions[2]);

            client.Dispose();
            server.Dispose();
        }

        [Fact]
        public void EndToEnd_BeforeConnection() {
            var revisions = new Dictionary<UInt64, UInt64>();
            var values = new Dictionary<UInt64, String>();

            var server = new WebPubSubServer("http://+:8000/");
            server.Publish(new StringMessage("Topic 0, message 1"), 0);
            server.Publish(new StringMessage("Topic 1, message 1"), 1);
            server.Publish(new StringMessage("Topic 1, message 2"), 1);
            server.Publish(new StringMessage("Topic 2, message 1"), 2);

            var client = new WebPubSubClient("ws://localhost:8000/");
            client.Subscribe((UInt64 topic, UInt64 revision, StringMessage message) => {
                revisions[topic] = revision;
                values[topic] = message.Value;
            });
            
            Thread.Sleep(100);
            Assert.Equal("Topic 0, message 1", values[0]);
            Assert.Equal("Topic 1, message 2", values[1]);
            Assert.Equal("Topic 2, message 1", values[2]);
            Assert.Equal((UInt64)0, revisions[0]);
            Assert.Equal((UInt64)1, revisions[1]);
            Assert.Equal((UInt64)0, revisions[2]);

            client.Dispose();
            server.Dispose();
        }
    }
}
