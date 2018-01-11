﻿using InvertedTomato.IO.Messages;
using InvertedTomato.Net.Zeta;
using System;
using System.Diagnostics;
using System.Net;
using System.Threading;

namespace Demo {
    class Program {
        static void Main(String[] args) {
            var rnd = new Random();
            Trace.Listeners.Add(new Listener());

            var server = new ZetaUdpPublisher<StringMessage>(1000); // Listed on UDP port 1000

            var client = new ZetaUdpSubscriber<StringMessage>("127.0.0.1:1000", (topic, revision, payload) => {
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine($"> {topic}#{revision}={payload.Value}");
            });


            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("Sending payloads...");
            server.Publish(0, new StringMessage("Topic 0, message 1"));
            server.Publish(1, new StringMessage("Topic 1, message 1"));
            server.Publish(1, new StringMessage("Topic 1, message 2"));
            server.Publish(2, new StringMessage("Topic 2, message 1"));

            for(Byte i = 2; i <= 50; i++) {
                server.Publish(1, new StringMessage($"Topic 1, message {i}"));
                Thread.Sleep(rnd.Next(0, 1000));
            }

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("Killing client.");
            client.Dispose();
            
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("Done.");
            
            Console.ReadKey(true);
        }
    }
}