using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AgctorSDK.Core.Adapters;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Resolution.Actors;
using AgctorSDK.Core.ProjectMemory.Resolution.Bridge;
using AgctorSDK.Core.ProjectMemory.Resolution.Messages;
using AgctorSDK.Core.ProjectMemory.Resolution.Models;
using AgctorSDK.Core.ProjectMemory.Resolution.Observability;
using AgctorSDK.Core.ProjectMemory.Resolution.Persistence;
using AgctorSDK.Core.ProjectMemory.Resolution.Review;
using AgctorSDK.Core.ProjectMemory.Resolution.Signals;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.IntegrationTests.ProjectMemory.Resolution;

/// <summary>
/// Covers the end-to-end behaviors added in Phases 3-6:
/// - Session summary drives a cross-session soft link
/// - Sidecar sink materializes an outgoing.yaml proposal
/// - Metrics counters advance through the pipeline
/// - Supervisor shutdown + re-spawn rehydrates state from disk
/// - Review query + service promote an edge through the runtime.
/// </summary>
[TestClass]
public sealed class ResolutionLifecycleIntegrationTests
{
    private string _root = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "res-lifecycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "people", "raha"));
        Directory.CreateDirectory(Path.Combine(_root, "people", "ryan"));
    }

    [TestCleanup]
    public void Teardown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private EntityRecord MakeEntity(string key, string display, params string[] aliases)
    {
        var root = Path.Combine(_root, "people", key);
        Directory.CreateDirectory(root);
        return new EntityRecord
        {
            EntityKey = key,
            EntityType = "person",
            RootPath = root,
            Metadata = new EntityMetadata
            {
                EntityKey = key, EntityType = "person", DisplayName = display,
                Aliases = new List<string>(aliases)
            }
        };
    }

    private static List<ISignalProducer> DefaultProducers() => new()
    {
        new AliasMatcher(),
        new SurfaceUniqueness(),
        new NegativeAssertions(),
        new GraphConsistency()
    };

    [TestMethod]
    public async Task SessionSummary_Creates_SoftLink_Sidecar_And_Bumps_Metrics()
    {
        using var runtime = new InMemoryActorRuntime();
        await runtime.InitializeAsync(new Dictionary<string, object>());

        var entities = new List<EntityRecord>
        {
            MakeEntity("raha", "Raha", "Raha Mohebbi"),
            MakeEntity("ryan", "Ryan")
        };

        var policy = new ResolutionPolicy { Enabled = true };
        // Lower the soft threshold for this test so alias+uniqueness alone cross it; Phase 3 signals
        // that would raise confidence further (attrOverlap, embedding) are covered elsewhere.
        policy.SoftThreshold = 0.40;
        var metrics = new ResolutionMetrics();
        var addressing = new DefaultResolutionAddressing();

        // Sidecar sink resolves "ryan" -> its folder so outgoing.yaml lands there.
        var sink = new SidecarIntentSink(host => host == "ryan" ? Path.Combine(_root, "people", "ryan") : null);

        var sup = await runtime.SpawnActorAsync(addressing.SupervisorIdFor("p1"),
            (id) => new ResolutionSupervisorActor(id, "p1", _root, runtime, policy, DefaultProducers(), addressing, sink, metrics));
        await sup.SpawnAllAsync(entities);

        var mention = new MentionRef
        {
            MentionId = "scenario:s1:ryan#relationships.family[0]",
            Scope = ResolutionScope.Scenario("s1"),
            SurfaceForm = "Raha",
            WithinEntityKey = "ryan",
            SessionId = "sess-B"
        };

        // Deliver via SessionSummary to also exercise that code path.
        await runtime.SendMessageAsync(addressing.ReconcilerIdFor("p1"), new SessionSummary
        {
            SessionId = "sess-B",
            ProjectId = "p1",
            Mentions = new List<MentionRef> { mention }
        }, senderId: "test");

        await WaitForAsync(ResolutionPaths.IncomingPath(Path.Combine(_root, "people", "raha")), TimeSpan.FromSeconds(3));

        var raha = new ResolutionEdgeStore(Path.Combine(_root, "people", "raha")).Load();
        Assert.AreEqual(1, raha.Edges.Count);
        Assert.AreEqual(ResolutionEdgeState.Soft, raha.Edges[0].State);

        var outgoing = Path.Combine(_root, "people", "ryan", ResolutionPaths.ResolutionFolder, "outgoing.yaml");
        await WaitForAsync(outgoing, TimeSpan.FromSeconds(3));
        Assert.IsTrue(File.Exists(outgoing), "sidecar intent sink should have written outgoing.yaml");

        Assert.IsTrue(metrics.Get(ResolutionMetrics.Keys.MentionsObserved("p1")) >= 1);
        Assert.IsTrue(metrics.Get(ResolutionMetrics.Keys.CandidatesDispatched("p1")) >= 1);
        Assert.IsTrue(metrics.Get(ResolutionMetrics.Keys.EdgesCreated("p1")) >= 1);
        Assert.IsTrue(metrics.Get(ResolutionMetrics.Keys.IntentsEmitted("p1")) >= 1);

        await sup.ShutdownAllAsync();
    }

    [TestMethod]
    public async Task Coalesce_Window_Suppresses_Duplicate_Dispatch()
    {
        using var runtime = new InMemoryActorRuntime();
        await runtime.InitializeAsync(new Dictionary<string, object>());

        var entities = new List<EntityRecord> { MakeEntity("raha", "Raha") };
        var policy = new ResolutionPolicy { Enabled = true };
        policy.Reconciler.CoalesceWindowMs = 10_000;   // long window so both messages fall inside it
        var metrics = new ResolutionMetrics();
        var addressing = new DefaultResolutionAddressing();

        var sup = await runtime.SpawnActorAsync(addressing.SupervisorIdFor("p1"),
            (id) => new ResolutionSupervisorActor(id, "p1", _root, runtime, policy, DefaultProducers(), addressing, null, metrics));
        await sup.SpawnAllAsync(entities);

        var mention = new MentionRef
        {
            MentionId = "m1", SurfaceForm = "Raha", WithinEntityKey = "ryan"
        };

        for (int i = 0; i < 3; i++)
            await runtime.SendMessageAsync(addressing.ReconcilerIdFor("p1"), new MentionObserved { Mention = mention }, senderId: "t");

        await Task.Delay(300);

        Assert.AreEqual(3, metrics.Get(ResolutionMetrics.Keys.MentionsObserved("p1")));
        Assert.AreEqual(1, metrics.Get(ResolutionMetrics.Keys.CandidatesDispatched("p1")), "only first dispatch survives");
        Assert.AreEqual(2, metrics.Get(ResolutionMetrics.Keys.CandidatesCoalesced("p1")));

        await sup.ShutdownAllAsync();
    }

    [TestMethod]
    public async Task Rehydration_After_Restart_Preserves_State()
    {
        using var runtime = new InMemoryActorRuntime();
        await runtime.InitializeAsync(new Dictionary<string, object>());

        var entities = new List<EntityRecord> { MakeEntity("raha", "Raha") };
        var policy = new ResolutionPolicy { Enabled = true };
        var addressing = new DefaultResolutionAddressing();

        // First "run" writes an edge.
        var sup1 = await runtime.SpawnActorAsync(addressing.SupervisorIdFor("p1"),
            (id) => new ResolutionSupervisorActor(id, "p1", _root, runtime, policy, DefaultProducers(), addressing));
        await sup1.SpawnAllAsync(entities);

        await runtime.SendMessageAsync(addressing.ReconcilerIdFor("p1"), new MentionObserved
        {
            Mention = new MentionRef { MentionId = "m1", SurfaceForm = "Raha", WithinEntityKey = "ryan" }
        }, senderId: "t");
        await WaitForAsync(ResolutionPaths.IncomingPath(Path.Combine(_root, "people", "raha")), TimeSpan.FromSeconds(3));
        await sup1.ShutdownAllAsync();

        // Second "run" (same disk, fresh actors) should still see the edge persisted.
        var sup2 = await runtime.SpawnActorAsync("ressup2",
            (id) => new ResolutionSupervisorActor(id, "p1", _root, runtime, policy, DefaultProducers(), addressing));
        await sup2.SpawnAllAsync(entities);

        var q = new ResolutionReviewQuery(entities);
        var pending = q.Pending();
        Assert.IsTrue(pending.Count >= 1, "rehydrated review query should see the soft link");
        Assert.AreEqual("m1", pending[0].Edge.Mention.MentionId);

        await sup2.ShutdownAllAsync();
    }

    [TestMethod]
    public async Task ReviewService_Promotes_Edge_Through_Runtime()
    {
        using var runtime = new InMemoryActorRuntime();
        await runtime.InitializeAsync(new Dictionary<string, object>());

        var entities = new List<EntityRecord> { MakeEntity("raha", "Raha") };
        var policy = new ResolutionPolicy { Enabled = true };
        var addressing = new DefaultResolutionAddressing();

        var sup = await runtime.SpawnActorAsync(addressing.SupervisorIdFor("p1"),
            (id) => new ResolutionSupervisorActor(id, "p1", _root, runtime, policy, DefaultProducers(), addressing));
        await sup.SpawnAllAsync(entities);

        var mention = new MentionRef { MentionId = "m1", SurfaceForm = "Raha", WithinEntityKey = "ryan" };
        await runtime.SendMessageAsync(addressing.ReconcilerIdFor("p1"), new MentionObserved { Mention = mention }, senderId: "t");
        await WaitForAsync(ResolutionPaths.IncomingPath(Path.Combine(_root, "people", "raha")), TimeSpan.FromSeconds(3));

        var service = new ResolutionReviewService(runtime, addressing, "p1");
        var edgeId = ResolutionEdge.MakeEdgeId(mention, "raha");
        await service.PromoteAsync("raha", edgeId, "user:test", "looks good");

        // Give the resolution actor time to process the promotion.
        for (int i = 0; i < 40; i++)
        {
            var doc = new ResolutionEdgeStore(Path.Combine(_root, "people", "raha")).Load();
            if (doc.Edges.Count > 0 && doc.Edges[0].State == ResolutionEdgeState.Hard) break;
            await Task.Delay(50);
        }
        var final = new ResolutionEdgeStore(Path.Combine(_root, "people", "raha")).Load();
        Assert.AreEqual(ResolutionEdgeState.Hard, final.Edges[0].State);

        await sup.ShutdownAllAsync();
    }

    private static async Task WaitForAsync(string path, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path) && new FileInfo(path).Length > 0) return;
            await Task.Delay(40);
        }
    }
}
