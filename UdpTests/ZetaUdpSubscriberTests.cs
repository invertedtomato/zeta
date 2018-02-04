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
        
        [Fact]
        public void Starting() {
            var server = new ZetaUdpSubscriber();
            Assert.False(server.IsStarted);
            server.Start("127.0.0.1:56789");
            Assert.True(server.IsStarted);
            server.Dispose();
            Assert.False(server.IsStarted);
        }
    }
}
