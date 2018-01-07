using InvertedTomato.IO.Messages;
using System;
using System.Collections.Generic;
using System.Threading;

namespace InvertedTomato.WebPubSub {
    class Program {
        // netsh http add urlacl url=http://127.0.0.1:8000/ user=lappy
        static void Main(String[] args) {
            var revisions = new Dictionary<UInt64, UInt64>();
            var values = new Dictionary<UInt64, String>();

            var rnd = new Random();
            
            Console.Write("Starting server... ");
            var server = new WebPubSubServer("http://+:8000/");
            Console.WriteLine("done");

            Console.Write("Starting client... ");
            var client = new WebPubSubClient("ws://localhost:8000/");
            Console.WriteLine("done");

            Console.Write("Subscribing client... ");
            client.Subscribe((UInt64 topic, UInt64 revision, StringMessage message) => {
                revisions[topic] = revision;
                values[topic] = message.ToString();
            });
            Console.WriteLine("done");

            Console.WriteLine("Sending payloads...");
            server.Publish(new StringMessage("Topic 0, message 1"), 0);
            server.Publish(new StringMessage("Topic 1, message 1"), 1);
            server.Publish(new StringMessage("Topic 1, message 2"), 1);
            server.Publish(new StringMessage("Topic 2, message 1"), 2);

            for(var i = 2; i <= 50; i++) {
                server.Publish(new StringMessage($"Topic 1, message {i}"), 1);
                Thread.Sleep(rnd.Next(0, 1000));
            }

            Console.WriteLine("done");
            //Console.ReadKey(true);

            Console.Write("Stopping... ");
            client.Dispose();
            server.Dispose();
            Console.WriteLine("done");
        }
    }
}
