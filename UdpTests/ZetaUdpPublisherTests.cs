using InvertedTomato.Net.Zeta;
using Xunit;

namespace UdpTests {
    public class ZetaUdpPublisherTests {
        [Fact]
        public void Disposing() {
            var server = new ZetaUdpPublisher();
            Assert.False(server.IsDisposed);
            server.Dispose();
            Assert.True(server.IsDisposed);
        }
        
        [Fact]
        public void Starting() {
            var server = new ZetaUdpPublisher();
            Assert.False(server.IsStarted);
            server.Start(56789);
            Assert.True(server.IsStarted);
            server.Dispose();
            Assert.False(server.IsStarted);
        }
    }
}
