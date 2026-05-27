using AgctorSDK.Core.Ollama;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.ProjectMemory.Visual;

[TestClass]
public sealed class OllamaThinkBlockStripperTests
{
    [TestMethod]
    public void Strip_removes_think_tags_and_redacted_blocks()
    {
        var raw = "\u003Credacted_thinking\u003Ehmm\u003C/redacted_thinking\u003E\n{\"memoryIntents\":[]}";
        var cleaned = OllamaThinkBlockStripper.Strip(raw);
        cleaned.Should().Contain("memoryIntents");
        cleaned.Should().NotContain("hmm");
    }
}
