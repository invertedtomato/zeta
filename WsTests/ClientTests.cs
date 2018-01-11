using InvertedTomato.IO.Messages;
using InvertedTomato.Net.Zeta;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace WsTests {
    public class ClientTests {
        [Fact]
        public void Subscribe() {
            var server = new WebPubSubServer("http://+:8001/");
            var client = new WebPubSubClient("ws://localhost:8001/");

            client.Subscribe((UInt32 topic, UInt32 revision, StringMessage message) => { }, 10, 20);
            client.Subscribe((UInt32 topic, UInt32 revision, StringMessage message) => { }, 0, 9);
            client.Subscribe((UInt32 topic, UInt32 revision, StringMessage message) => { }, 21, 30);

            Assert.Throws<InvalidOperationException>(() => {
                client.Subscribe((UInt32 topic, UInt32 revision, StringMessage message) => { }, 10, 20);
            });
            Assert.Throws<InvalidOperationException>(() => {
                client.Subscribe((UInt32 topic, UInt32 revision, StringMessage message) => { }, 0, 10);
            });
            Assert.Throws<InvalidOperationException>(() => {
                client.Subscribe((UInt32 topic, UInt32 revision, StringMessage message) => { }, 20, 30);
            });

            client.Dispose();
            server.Dispose();
        }
    }
}
