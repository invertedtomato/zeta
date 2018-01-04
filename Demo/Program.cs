using System;

namespace InvertedTomato.WebPubSub {
    class Program {
        // netsh http add urlacl url=http://127.0.0.1:8000/ user=lappy
        static void Main(String[] args) {
            Console.Write("Starting... ");
            var server = new WebPubSubServer("http://+:8000/");
            Console.WriteLine("done");

            Console.ReadKey(true);

            Console.Write("Stopping... ");
            server.Dispose();
            Console.WriteLine("done");
        }
    }
}
