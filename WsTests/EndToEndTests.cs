using InvertedTomato.IO.Messages;
using InvertedTomato.WebPubSub;
using System;
using System.Collections.Generic;
using System.Threading;
using Xunit;

namespace LibraryTests {
    public class EndToEndTests {
        [Fact]
        public void EndToEnd_AfterConnection_Basic() {
            var revisions = new Dictionary<UInt32, UInt32>();
            var values = new Dictionary<UInt32, String>();

            var server = new WebPubSubServer("http://+:8002/");
            var client = new WebPubSubClient("ws://localhost:8002/");
            client.Subscribe((UInt32 topic, UInt32 revision, StringMessage message) => {
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
            Assert.Equal((UInt32)0, revisions[0]);
            Assert.Equal((UInt32)1, revisions[1]);
            Assert.Equal((UInt32)0, revisions[2]);

            client.Dispose();
            Assert.True(client.IsDisposed);
            server.Dispose();
            Assert.True(server.IsDisposed);
        }

        [Fact]
        public void EndToEnd_AfterConnection_Flood() {
            var revisions = new Dictionary<UInt32, UInt32>();
            var values = new Dictionary<UInt32, String>();

            var server = new WebPubSubServer("http://+:8003/");
            var client = new WebPubSubClient("ws://localhost:8003/");
            client.Subscribe((UInt32 topic, UInt32 revision, StringMessage message) => {
                revisions[topic] = revision;
                values[topic] = message.Value;
            });

            Thread.Sleep(100);

            server.Publish(new StringMessage("Topic 0, message 1"), 0);
            server.Publish(new StringMessage("Topic 1, message 1"), 1);
            server.Publish(new StringMessage("Topic 1, message 2"), 1);
            server.Publish(new StringMessage("Topic 2, message 1"), 2);

            for(var i = 2; i <= 50; i++) {
                server.Publish(new StringMessage($"Topic 1, message {i}"), 1);
            }

            Thread.Sleep(100);
            Assert.Equal("Topic 0, message 1", values[0]);
            Assert.Equal("Topic 1, message 50", values[1]);
            Assert.Equal("Topic 2, message 1", values[2]);
            Assert.Equal((UInt32)0, revisions[0]);
            Assert.Equal((UInt32)50, revisions[1]);
            Assert.Equal((UInt32)0, revisions[2]);

            client.Dispose();
            Assert.True(client.IsDisposed);
            server.Dispose();
            Assert.True(server.IsDisposed);
        }

        [Fact]
        public void EndToEnd_BeforeConnection_Basic() {
            var revisions = new Dictionary<UInt32, UInt32>();
            var values = new Dictionary<UInt32, String>();

            var server = new WebPubSubServer("http://+:8004/");
            server.Publish(new StringMessage("Topic 0, message 1"), 0);
            server.Publish(new StringMessage("Topic 1, message 1"), 1);
            server.Publish(new StringMessage("Topic 1, message 2"), 1);
            server.Publish(new StringMessage("Topic 2, message 1"), 2);

            var client = new WebPubSubClient("ws://localhost:8004/");
            client.Subscribe((UInt32 topic, UInt32 revision, StringMessage message) => {
                revisions[topic] = revision;
                values[topic] = message.Value;
            });

            Thread.Sleep(100);
            Assert.Equal("Topic 0, message 1", values[0]);
            Assert.Equal("Topic 1, message 2", values[1]);
            Assert.Equal("Topic 2, message 1", values[2]);
            Assert.Equal((UInt32)0, revisions[0]);
            Assert.Equal((UInt32)1, revisions[1]);
            Assert.Equal((UInt32)0, revisions[2]);

            client.Dispose();
            Assert.True(client.IsDisposed);
            server.Dispose();
            Assert.True(server.IsDisposed);
        }

        [Fact]
        public void EndToEnd_BeforeConnection_Flood() {
            var revisions = new Dictionary<UInt32, UInt32>();
            var values = new Dictionary<UInt32, String>();

            var server = new WebPubSubServer("http://+:8005/");
            server.Publish(new StringMessage("Topic 0, message 1"), 0);
            server.Publish(new StringMessage("Topic 1, message 1"), 1);
            server.Publish(new StringMessage("Topic 1, message 2"), 1);
            server.Publish(new StringMessage("Topic 2, message 1"), 2);
            for(var i = 2; i <= 50; i++) {
                server.Publish(new StringMessage($"Topic 1, message {i}"), 1);
            }

            var client = new WebPubSubClient("ws://localhost:8005/");
            client.Subscribe((UInt32 topic, UInt32 revision, StringMessage message) => {
                revisions[topic] = revision;
                values[topic] = message.Value;
            });

            Thread.Sleep(100);
            Assert.Equal("Topic 0, message 1", values[0]);
            Assert.Equal("Topic 1, message 50", values[1]);
            Assert.Equal("Topic 2, message 1", values[2]);
            Assert.Equal((UInt32)0, revisions[0]);
            Assert.Equal((UInt32)50, revisions[1]);
            Assert.Equal((UInt32)0, revisions[2]);

            client.Dispose();
            Assert.True(client.IsDisposed);
            server.Dispose();
            Assert.True(server.IsDisposed);
        }

        [Fact]
        public void EndToEnd_AfterConnection_Groups() {
            var revisions = new Dictionary<UInt32, UInt32>();
            var values = new Dictionary<UInt32, String>();

            var server = new WebPubSubServer("http://+:8006/");
            var client = new WebPubSubClient("ws://localhost:8006/", new UInt32[] { 1 });
            client.Subscribe((UInt32 topic, UInt32 revision, StringMessage message) => {
                revisions[topic] = revision;
                values[topic] = message.Value;
            });

            Thread.Sleep(100);

            server.Publish(new StringMessage("Topic 0, message 1"), 0, 1);
            server.Publish(new StringMessage("Topic 1, message 1"), 1);
            server.Publish(new StringMessage("Topic 1, message 2"), 1);
            server.Publish(new StringMessage("Topic 2, message 1"), 2);

            Thread.Sleep(100);
            Assert.Single(values);
            Assert.Equal("Topic 0, message 1", values[0]);
            Assert.Equal((UInt32)0, revisions[0]);

            client.Dispose();
            Assert.True(client.IsDisposed);
            server.Dispose();
            Assert.True(server.IsDisposed);
        }
    }
}
