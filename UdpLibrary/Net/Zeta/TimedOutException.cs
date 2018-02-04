using System;

namespace InvertedTomato.Net.Zeta {
    public class TimedOutException : Exception {
        public TimedOutException() { }
        public TimedOutException(String message) : base(message) { }
        public TimedOutException(String message, Exception innerException) : base(message, innerException) { }
    }
}