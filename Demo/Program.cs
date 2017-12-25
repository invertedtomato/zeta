using InvertedTomato.Zeta;
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
            server.Publish(1, new Byte[] { 2 });

            for(byte i = 3; i < 100; i++) {
                Thread.Sleep(rnd.Next(0, 2000));
                server.Publish(1, new byte[] { i });
            }

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("Done.");
            Console.ReadKey(true);
        }
    }
}
