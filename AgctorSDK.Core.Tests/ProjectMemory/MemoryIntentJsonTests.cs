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

    [TestMethod]
    public void TryParseBatch_OptionalScenarioId_RoundTrips()
    {
        const string json = """{"scenarioId":"people","memoryIntents":[{"entityKey":"raha","knowledgeType":"identity","value":"x","confidence":1}]}""";
        Assert.IsTrue(MemoryIntentJson.TryParseBatch(json, out var batch, out var err), err);
        Assert.AreEqual("people", batch!.ScenarioId);
        Assert.AreEqual(1, batch.MemoryIntents.Count);
    }

    [TestMethod]
    public void TryParseBatch_ExtractsBatchWhenProseWrapsJson()
    {
        var raw = """
            Here is the extraction:

            {"memoryIntents":[{"entityKey":"melody","knowledgeType":"skill","attribute":"","value":"walking","confidence":0.9}]}

            Thanks.
            """;
        Assert.IsTrue(MemoryIntentJson.TryParseBatch(raw, out var batch, out var err), err);
        Assert.AreEqual(1, batch!.MemoryIntents.Count);
        Assert.AreEqual("melody", batch.MemoryIntents[0].EntityKey);
        Assert.AreEqual("skill", batch.MemoryIntents[0].KnowledgeType);
    }

    [TestMethod]
    public void TryParseBatch_RootArray_WrapsAndNormalizesEntityKeys()
    {
        const string json = """
            [
              {"entityKey":"match/people/melody/","knowledgeType":"profile_fact","attribute":"name","value":"Melody","confidence":1},
              {"entityKey":"match/people/raha/","knowledgeType":"family_role","attribute":"brother","value":"Melody","confidence":1}
            ]
            """;
        Assert.IsTrue(MemoryIntentJson.TryParseBatch(json, out var batch, out var err), err);
        Assert.AreEqual(2, batch!.MemoryIntents.Count);
        Assert.AreEqual("melody", batch.MemoryIntents[0].EntityKey);
        Assert.AreEqual("raha", batch.MemoryIntents[1].EntityKey);
    }

    [TestMethod]
    public void TryParseBatch_IntentsAlias_RoundTrips()
    {
        const string json = """{"scenarioId":"people","intents":[{"entityKey":"x","knowledgeType":"skill","value":"y","confidence":0.5}]}""";
        Assert.IsTrue(MemoryIntentJson.TryParseBatch(json, out var batch, out var err), err);
        Assert.AreEqual("people", batch!.ScenarioId);
        Assert.AreEqual(1, batch.MemoryIntents.Count);
        Assert.AreEqual("x", batch.MemoryIntents[0].EntityKey);
    }

    [TestMethod]
    public void TryParseBatch_FencedArray_Parses()
    {
        var raw = """
            ```json
            [{"entityKey":"a","knowledgeType":"t","value":"v","confidence":1}]
            ```
            """;
        Assert.IsTrue(MemoryIntentJson.TryParseBatch(raw, out var batch, out var err), err);
        Assert.AreEqual(1, batch!.MemoryIntents.Count);
        Assert.AreEqual("a", batch.MemoryIntents[0].EntityKey);
    }

    [TestMethod]
    public void TryParseBatch_NumberValue_CoercesToString()
    {
        const string json = """
            {
              "memoryIntents": [
                {"entityKey":"melody","knowledgeType":"profile_fact","attribute":"age","value":47,"confidence":1}
              ]
            }
            """;
        Assert.IsTrue(MemoryIntentJson.TryParseBatch(json, out var batch, out var err), err);
        Assert.AreEqual("47", batch!.MemoryIntents[0].Value);
    }
}
