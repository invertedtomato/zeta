using System;

namespace InvertedTomato.Net.Zeta {
    public static class Constants {
        public static readonly Byte VERSION = 0x02;
        public static readonly Int32 SERVERTXHEADER_LENGTH = 4 + 4 + 2; // <typecode> <topic> <revision>
        public static readonly Int32 CLIENTTXHEADER_LENGTH = 1 + 16; // <version> <authorization>
    }
}
