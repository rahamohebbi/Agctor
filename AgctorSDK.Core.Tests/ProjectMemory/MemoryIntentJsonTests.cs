using AgctorSDK.Core.ProjectMemory.Orchestration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.ProjectMemory;

[TestClass]
public sealed class MemoryIntentJsonTests
{
    [TestMethod]
    public void UnwrapMarkdownFences_StripsJsonFence()
    {
        var raw = """
            ```json
            {"memoryIntents":[{"entityKey":"a","knowledgeType":"x","value":"v","confidence":1}]}
            ```
            """;
        var inner = MemoryIntentJson.UnwrapMarkdownFences(raw);
        StringAssert.StartsWith(inner, "{");
        Assert.IsTrue(MemoryIntentJson.TryParseBatch(inner, out var batch, out var err), err);
        Assert.AreEqual(1, batch!.MemoryIntents.Count);
        Assert.AreEqual("a", batch.MemoryIntents[0].EntityKey);
    }

    [TestMethod]
    public void TryParseBatch_Invalid_ReturnsFalse()
    {
        Assert.IsFalse(MemoryIntentJson.TryParseBatch("not json", out _, out var err));
        Assert.IsNotNull(err);
    }
}
