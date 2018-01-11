using InvertedTomato.IO.Messages;
using System;
using System.Collections.Generic;
using System.Threading;

namespace InvertedTomato.WebPubSub {
    class Program {
        static void Main(String[] args) {
            //netsh http add urlacl url=http://*:8000/ user=Everyone
            var revisions = new Dictionary<UInt32, UInt32>();
            var values = new Dictionary<UInt32, String>();

            var rnd = new Random();
            
            Console.Write("Starting server... ");
            var server = new WebPubSubServer("http://+:8000/");
            Console.WriteLine("done");

            Console.Write("Starting client... ");
            var client = new WebPubSubClient("ws://localhost:8000/");
            Console.WriteLine("done");

            Console.Write("Subscribing client... ");
            client.Subscribe((UInt32 topic, UInt32 revision, StringMessage message) => {
                revisions[topic] = revision;
                values[topic] = message.ToString();
                Console.WriteLine($"  {topic}#{revision}: {message}");
            });
            Console.WriteLine("done");

            Console.WriteLine("Sending payloads...");
            server.Publish(new StringMessage("Topic 0, message 1"), 0);
            server.Publish(new StringMessage("Topic 1, message 1"), 1);
            server.Publish(new StringMessage("Topic 1, message 2"), 1);
            server.Publish(new StringMessage("Topic 2, message 1"), 2);

            for(var i = 2; i <= 50; i++) {
                server.Publish(new StringMessage($"Topic 1, message {i}"), 1);
                if(rnd.Next(0, 10) > 5) {
                    Thread.Sleep(1);
                }
            }

            Thread.Sleep(1000);
            Console.WriteLine("done");
            Console.ReadKey(true);
            
            Console.Write("Stopping... ");
            client.Dispose();
            server.Dispose();
            Console.WriteLine("done");
        }
    }
}
