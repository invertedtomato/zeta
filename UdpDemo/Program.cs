using InvertedTomato.IO.Messages;
using InvertedTomato.Net.Zeta;
using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Demo {
    class Program {
        static void Main(String[] args) {
            var rnd = new Random();
            Trace.Listeners.Add(new Listener());

            // Start server
            var server = new ZetaUdpPublisher(); 
            server.Start(1000); // Listed on UDP port 1000

            // Start client
            var client = new ZetaUdpSubscriber();
            client.Start("127.0.0.1:1000");

            // Start reading
            var readerThread = Task.Run(() => {
                while (!client.IsDisposed) {
                    var msg = client.Read<StringMessage>();
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.WriteLine($"> {msg.Value}");
                }
            });

            // Send payloads
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("Sending payloads...");
            server.Publish(new StringMessage("Topic 0, message 1"), 0);
            server.Publish(new StringMessage("Topic 1, message 1"), 1);
            server.Publish(new StringMessage("Topic 1, message 2"), 1);
            server.Publish(new StringMessage("Topic 2, message 1"), 2);

            for (Byte i = 2; i <= 50; i++) {
                server.Publish(new StringMessage($"Topic 1, message {i}"), 1);
                Thread.Sleep(rnd.Next(0, 1000));
            }

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("Killing client.");
            readerThread.Dispose();
            client.Dispose();

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("Done.");

            Console.ReadKey(true);
        }
    }
}
