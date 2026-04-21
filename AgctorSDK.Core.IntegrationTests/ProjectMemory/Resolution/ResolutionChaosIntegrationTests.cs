using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AgctorSDK.Core.Adapters;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Resolution.Actors;
using AgctorSDK.Core.ProjectMemory.Resolution.Messages;
using AgctorSDK.Core.ProjectMemory.Resolution.Models;
using AgctorSDK.Core.ProjectMemory.Resolution.Persistence;
using AgctorSDK.Core.ProjectMemory.Resolution.Signals;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.IntegrationTests.ProjectMemory.Resolution;

/// <summary>
/// PRD-018 §6 acceptance criterion 7 (resilience): killing the supervisor mid-batch must not
/// drop evidence or leave duplicate rows on disk. We force a shutdown between two waves of
/// mentions and assert the on-disk edge is consistent after a fresh spawn.
/// </summary>
[TestClass]
public sealed class ResolutionChaosIntegrationTests
{
    private string _root = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "res-chaos-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "people", "raha"));
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
        new AliasMatcher(), new SurfaceUniqueness(), new NegativeAssertions()
    };

    [TestMethod]
    public async Task Kill_Supervisor_MidBatch_Then_Rehydrate_Has_One_Edge_Per_Mention()
    {
        using var runtime = new InMemoryActorRuntime();
        await runtime.InitializeAsync(new Dictionary<string, object>());

        var entities = new List<EntityRecord> { MakeEntity("raha", "Raha", "Raha Mohebbi") };
        var policy = new ResolutionPolicy { Enabled = true };
        var addressing = new DefaultResolutionAddressing();

        var sup1 = await runtime.SpawnActorAsync(addressing.SupervisorIdFor("p1"),
            id => new ResolutionSupervisorActor(id, "p1", _root, runtime, policy, DefaultProducers(), addressing));
        await sup1.SpawnAllAsync(entities);

        var mention = new MentionRef { MentionId = "m-chaos", SurfaceForm = "Raha", WithinEntityKey = "ryan" };
        for (int i = 0; i < 5; i++)
            await runtime.SendMessageAsync(addressing.ReconcilerIdFor("p1"), new MentionObserved { Mention = mention }, senderId: "chaos");

        // Wait until at least one edge is written, then kill the supervisor while more
        // coalesced candidates could still theoretically fire.
        await WaitForAsync(ResolutionPaths.IncomingPath(Path.Combine(_root, "people", "raha")), TimeSpan.FromSeconds(3));
        await sup1.ShutdownAllAsync();

        // Rehydrate and send more duplicates to simulate the retry-after-restart case.
        var sup2 = await runtime.SpawnActorAsync("sup2",
            id => new ResolutionSupervisorActor(id, "p1", _root, runtime, policy, DefaultProducers(), addressing));
        await sup2.SpawnAllAsync(entities);
        for (int i = 0; i < 3; i++)
            await runtime.SendMessageAsync(addressing.ReconcilerIdFor("p1"), new MentionObserved { Mention = mention }, senderId: "chaos");

        await Task.Delay(300);

        var store = new ResolutionEdgeStore(Path.Combine(_root, "people", "raha"));
        var doc = store.Load();
        // Same mention should never produce duplicate edges — the edgeId is deterministic.
        Assert.AreEqual(1, doc.Edges.Count);
        Assert.AreEqual("m-chaos->entity:raha", doc.Edges[0].EdgeId);

        // Signal rows are deduped by (kind, inputsFingerprint) so count stays bounded.
        var aliasSignals = doc.Edges[0].Signals.Count(s => s.Kind == "aliasMatch");
        Assert.IsTrue(aliasSignals <= 2, $"expected <=2 aliasMatch rows after chaos, got {aliasSignals}");

        await sup2.ShutdownAllAsync();
    }

    [TestMethod]
    public async Task ReloadPolicy_Swaps_Thresholds_Across_Supervisor()
    {
        using var runtime = new InMemoryActorRuntime();
        await runtime.InitializeAsync(new Dictionary<string, object>());

        var entities = new List<EntityRecord> { MakeEntity("raha", "Raha") };
        var policy = new ResolutionPolicy { Enabled = true };
        policy.SoftThreshold = 0.95;    // too strict for the default signals to pass initially
        var addressing = new DefaultResolutionAddressing();

        var sup = await runtime.SpawnActorAsync(addressing.SupervisorIdFor("p1"),
            id => new ResolutionSupervisorActor(id, "p1", _root, runtime, policy, DefaultProducers(), addressing));
        await sup.SpawnAllAsync(entities);

        var mention = new MentionRef { MentionId = "m-reload", SurfaceForm = "Raha", WithinEntityKey = "ryan" };
        await runtime.SendMessageAsync(addressing.ReconcilerIdFor("p1"), new MentionObserved { Mention = mention }, senderId: "t");
        await WaitForAsync(ResolutionPaths.IncomingPath(Path.Combine(_root, "people", "raha")), TimeSpan.FromSeconds(3));

        // Hot swap: lower the soft threshold so the next confidence recomputation is actionable.
        var relaxed = new ResolutionPolicy { Enabled = true };
        relaxed.SoftThreshold = 0.10;
        await sup.ReloadPolicyAsync(relaxed, changedBy: "test");

        // A second mention after reload should land too — no restart needed.
        var mention2 = new MentionRef { MentionId = "m-reload2", SurfaceForm = "Raha", WithinEntityKey = "ryan" };
        await runtime.SendMessageAsync(addressing.ReconcilerIdFor("p1"), new MentionObserved { Mention = mention2 }, senderId: "t");
        await Task.Delay(300);

        var doc = new ResolutionEdgeStore(Path.Combine(_root, "people", "raha")).Load();
        Assert.IsTrue(doc.Edges.Count >= 2, $"expected >=2 edges after reload, got {doc.Edges.Count}");

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
