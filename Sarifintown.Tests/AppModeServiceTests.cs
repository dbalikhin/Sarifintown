using Sarifintown.Services;

namespace Sarifintown.Tests
{
    [TestFixture]
    public class AppModeServiceTests
    {
        [TestCase("https://localhost:5001/mcp/dashboard")]
        [TestCase("https://localhost:5001/MCP/files")]
        public void IsMcpHostedRoute_WhenUriTargetsMcpPrefix_ReturnsTrue(string absoluteUri)
        {
            var result = AppModeService.IsMcpHostedRoute(absoluteUri);

            Assert.That(result, Is.True);
        }

        [TestCase("https://localhost:5001/")]
        [TestCase("https://localhost:5001/analysis")]
        public void IsMcpHostedRoute_WhenUriDoesNotTargetMcpPrefix_ReturnsFalse(string absoluteUri)
        {
            var result = AppModeService.IsMcpHostedRoute(absoluteUri);

            Assert.That(result, Is.False);
        }

        [Test]
        public void IsMcpHostedRoute_WhenUriMissing_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => AppModeService.IsMcpHostedRoute("  "));
        }
    }
}
