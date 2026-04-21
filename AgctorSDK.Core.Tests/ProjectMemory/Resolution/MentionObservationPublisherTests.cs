using System.Collections.Generic;
using System.Threading.Tasks;
using AgctorSDK.Core.Adapters;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Resolution.Bridge;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.ProjectMemory.Resolution;

/// <summary>
/// MentionObservationPublisher does two things worth guarding: it turns extractor intents into
/// MentionRefs deterministically, and — when given a session id — records the mentions on the
/// accumulator so a later SessionSummary carries the same facts.
/// </summary>
[TestClass]
public sealed class MentionObservationPublisherTests
{
    [TestMethod]
    public void FromMemoryIntents_Extracts_Capitalized_Names_With_Scope()
    {
        var intents = new List<MemoryIntent>
        {
            new() { EntityKey = "ryan", KnowledgeType = "relationships", Attribute = "family", Value = "Father: Raha Mohebbi" },
            new() { EntityKey = "ryan", KnowledgeType = "profile", Attribute = "workplace", Value = "works at Acme" }
        };

        var mentions = MentionObservationPublisher.FromMemoryIntents(intents, scenarioId: "s1", sessionId: "sess-1", turnId: "t-3");

        // "Raha Mohebbi", "Acme" (NameToken regex matches capitalized single/pair tokens).
        Assert.IsTrue(mentions.Count >= 1);
        foreach (var m in mentions)
        {
            Assert.AreEqual("ryan", m.WithinEntityKey);
            Assert.AreEqual("scenario", m.Scope.Kind);
            Assert.AreEqual("s1", m.Scope.ScenarioId);
            Assert.AreEqual("sess-1", m.SessionId);
            Assert.AreEqual("t-3", m.TurnId);
        }
    }

    [TestMethod]
    public async Task PublishAsync_Records_On_Accumulator_When_SessionId_Set()
    {
        using var rt = new InMemoryActorRuntime();
        await rt.InitializeAsync(new Dictionary<string, object>());
        var acc = new SessionMentionAccumulator();
        var publisher = new MentionObservationPublisher(rt, addressing: null, accumulator: acc);

        var mentions = new[]
        {
            new AgctorSDK.Core.ProjectMemory.Resolution.Models.MentionRef
            {
                MentionId = "m1", SurfaceForm = "Raha", WithinEntityKey = "ryan", SessionId = "sess-xyz"
            }
        };

        await publisher.PublishAsync("p1", mentions);

        var snap = acc.Snapshot("sess-xyz");
        Assert.AreEqual(1, snap.Count);
        Assert.AreEqual("Raha", snap[0].SurfaceForm);
    }
}
