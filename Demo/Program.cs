using InvertedTomato.Net.Zeta;
using System;
using System.Diagnostics;
using System.Net;
using System.Threading;

namespace Demo {
    class Program {
        static void Main(string[] args) {
            var rnd = new Random();
            Trace.Listeners.Add(new Listener());

            var server = new ZetaServer(1000);

            var client = new ZetaClient(new IPEndPoint(IPAddress.Loopback, 1000), (topic, revision, value) => {
                if(value.Length != 1) {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine($"> {topic}#{revision}={value[0]}");
            });


            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("Sending payloads...");
            server.Publish(0, new Byte[] { 128 });
            server.Publish(1, new Byte[] { 0 });
            server.Publish(1, new Byte[] { 1 });
            server.Publish(2, new Byte[] { 255 });

            for(byte i = 2; i <= 50; i++) {
                server.Publish(1, new byte[] { i });
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
