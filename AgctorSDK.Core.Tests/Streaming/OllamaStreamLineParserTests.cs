using AgctorSDK.Core.Streaming;
using Xunit;

namespace AgctorSDK.Core.Tests.Streaming
{
    public class OllamaStreamLineParserTests
    {
        [Fact]
        public void TryParseLine_TokenAndNotDone_ReturnsToken()
        {
            var ok = OllamaStreamLineParser.TryParseLine(
                """{"model":"m","created_at":"t","response":"hel","done":false}""",
                out var token,
                out var done);

            Assert.True(ok);
            Assert.Equal("hel", token);
            Assert.False(done);
        }

        [Fact]
        public void TryParseLine_FinalChunk_MarksDone()
        {
            var ok = OllamaStreamLineParser.TryParseLine(
                """{"model":"m","created_at":"t","response":"lo","done":true}""",
                out var token,
                out var done);

            Assert.True(ok);
            Assert.Equal("lo", token);
            Assert.True(done);
        }

        [Fact]
        public void TryParseLine_EmptyLine_ReturnsFalse()
        {
            var ok = OllamaStreamLineParser.TryParseLine("   ", out _, out _);
            Assert.False(ok);
        }
    }
}
