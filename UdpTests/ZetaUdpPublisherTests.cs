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
    }
}
