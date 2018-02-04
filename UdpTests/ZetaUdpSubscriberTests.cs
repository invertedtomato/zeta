using InvertedTomato.Net.Zeta;
using Xunit;

namespace UdpTests {
    public class ZetaUdpSubscriberTests {
        [Fact]
        public void Disposing() {
            var server = new ZetaUdpSubscriber();
            Assert.False(server.IsDisposed);
            server.Dispose();
            Assert.True(server.IsDisposed);
        }
    }
}
