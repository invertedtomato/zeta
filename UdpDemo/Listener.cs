using System;
using System.Diagnostics;

namespace Demo {
    public class Listener : TraceListener {
        public override void Write(String message) {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write(message);
        }

        public override void WriteLine(String message) {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(message);
        }
    }
}