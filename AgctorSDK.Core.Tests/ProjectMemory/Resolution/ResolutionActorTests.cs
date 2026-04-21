using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.ProjectMemory.Resolution.Actors;
using AgctorSDK.Core.ProjectMemory.Resolution.Messages;
using AgctorSDK.Core.ProjectMemory.Resolution.Models;
using AgctorSDK.Core.ProjectMemory.Resolution.Persistence;
using AgctorSDK.Core.ProjectMemory.Resolution.Signals;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.ProjectMemory.Resolution;

[TestClass]
public sealed class ResolutionActorTests
{
    private string _root = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "res-actor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Teardown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private static ResolveCandidate Candidate(string surface, int total = 1, string? negation = null) => new()
    {
        Mention = new MentionRef { SurfaceForm = surface, MentionId = $"mention:{surface}" },
        CandidateEntityKey = "raha",
        CandidateEntityPath = "ignored",
        TotalEntitiesMatchingSurface = total,
        SessionNegativeAssertions = negation == null ? new List<string>() : new List<string> { negation }
    };

    private ResolutionActor MakeActor(ResolutionPolicy? policy = null)
    {
        policy ??= new ResolutionPolicy();
        var store = new ResolutionEdgeStore(_root);
        var producers = new List<ISignalProducer>
        {
            new AliasMatcher(),
            new SurfaceUniqueness(),
            new NegativeAssertions()
        };
        return new ResolutionActor("res:test:raha", "raha", "Raha", new[] { "Raha Mohebbi" }, store, policy, producers);
    }

    [TestMethod]
    public async Task Unique_Alias_Match_Creates_Soft_Edge_With_Expected_Confidence()
    {
        var actor = MakeActor();
        await actor.InitializeAsync();

        var env = new MessageEnvelope(Candidate("Raha"));
        var resp = await actor.ReceiveAsync(env);

        var ev = (EvidenceAppended)resp.Payload;
        Assert.AreEqual(ResolutionEdgeState.Soft, ev.State);
        Assert.IsTrue(ev.Confidence > 0.4, $"expected >0.4, got {ev.Confidence}");
        var doc = new ResolutionEdgeStore(_root).Load();
        Assert.AreEqual(1, doc.Edges.Count);
        Assert.IsTrue(doc.Edges[0].Signals.Count >= 2);
    }

    [TestMethod]
    public async Task Idempotent_Re_Observation_Does_Not_Duplicate_Signals()
    {
        var actor = MakeActor();
        await actor.InitializeAsync();
        var env = new MessageEnvelope(Candidate("Raha"));

        await actor.ReceiveAsync(env);
        await actor.ReceiveAsync(env);
        await actor.ReceiveAsync(env);

        var doc = new ResolutionEdgeStore(_root).Load();
        Assert.AreEqual(1, doc.Edges.Count);
        Assert.AreEqual(2, doc.Edges[0].Signals.Count, "alias + uniqueness only; negatives skipped");
    }

    [TestMethod]
    public async Task Operator_Promotion_Flips_To_Hard_And_Logs()
    {
        var actor = MakeActor();
        await actor.InitializeAsync();

        await actor.ReceiveAsync(new MessageEnvelope(Candidate("Raha")));
        var doc = new ResolutionEdgeStore(_root).Load();
        var edgeId = doc.Edges[0].EdgeId;

        var resp = await actor.ReceiveAsync(new MessageEnvelope(new PromotionRequested
        {
            EdgeId = edgeId, RequestedBy = "user:alice", Reason = "confident match"
        }));
        var changed = (LinkStateChanged)resp.Payload;
        Assert.AreEqual(ResolutionEdgeState.Hard, changed.To);

        var final = new ResolutionEdgeStore(_root).Load();
        Assert.AreEqual(ResolutionEdgeState.Hard, final.Edges[0].State);
        Assert.IsTrue(File.Exists(ResolutionPaths.PromotionsPath(_root)));
    }

    [TestMethod]
    public async Task Auto_Promotion_Requires_Confidence_And_Two_Families()
    {
        // Bump alias weight so a single signal can exceed hardThreshold by itself.
        var policy = new ResolutionPolicy();
        policy.SignalWeights["aliasMatch"] = 0.95;
        policy.HardThreshold = 0.90;

        var actor = MakeActor(policy);
        await actor.InitializeAsync();

        await actor.ReceiveAsync(new MessageEnvelope(Candidate("Raha")));
        var doc = new ResolutionEdgeStore(_root).Load();
        // Two families (alias + uniqueness) should both fire; confidence >= 0.90.
        Assert.IsTrue(doc.Edges[0].Confidence >= 0.90, $"got {doc.Edges[0].Confidence}");
        Assert.AreEqual(ResolutionEdgeState.Hard, doc.Edges[0].State, "should auto-promote");
        Assert.IsTrue(File.Exists(ResolutionPaths.PromotionsPath(_root)));
    }

    [TestMethod]
    public async Task Negative_Assertion_Caps_Confidence_And_Blocks_Auto_Promote()
    {
        var policy = new ResolutionPolicy();
        policy.SignalWeights["aliasMatch"] = 0.95;
        policy.HardThreshold = 0.90;

        var actor = MakeActor(policy);
        await actor.InitializeAsync();

        await actor.ReceiveAsync(new MessageEnvelope(Candidate("Raha", negation: "different Raha")));
        var doc = new ResolutionEdgeStore(_root).Load();
        Assert.AreEqual(ResolutionEdgeState.Soft, doc.Edges[0].State);
        Assert.IsTrue(doc.Edges[0].Confidence < policy.HardThreshold, $"got {doc.Edges[0].Confidence}");
    }
}
