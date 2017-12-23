using InvertedTomato.Zeta;
using System;
using System.Net;
using System.Threading;

namespace Demo {
    class Program {
        static void Main(string[] args) {
            var expected = 0;

            var server = new ZetaServer(1000, (endpoint, authorization) => {
                if( authorization.Length == 1 && authorization[0] == 128) {
                    Console.WriteLine("Auth request approved.");
                    return true;
                } else {
                    Console.WriteLine("Auth request rejected.");
                    return false;
                }
            });

            var client = new ZetaClient(new IPEndPoint(IPAddress.Loopback, 1000), new Byte[] { 128 }, (topic, revision, value) => {
                if(value.Length != 1) {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                Console.WriteLine($"Received {topic}#{revision}={value[0]}.");

                expected++;
            });

            Thread.Sleep(500);

            Console.WriteLine("Sending payloads...");
            server.Publish(1, new Byte[] { 0 });
            server.Publish(2, new Byte[] { 255 });
            Thread.Sleep(10);
            server.Publish(1, new Byte[] { 1 });
            Thread.Sleep(10);
            server.Publish(1, new Byte[] { 2 });

            Console.ReadKey(true);
        }
    }
}
