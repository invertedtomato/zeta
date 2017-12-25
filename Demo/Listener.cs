using System;
using System.Diagnostics;

namespace Demo {
    public class Listener : TraceListener {
        public override void Write(string message) {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write(message);
        }

        public override void WriteLine(string message) {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(message);
        }
    }
}