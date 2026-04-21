using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AgctorSDK.Core.Adapters;
using AgctorSDK.Core.Interfaces;
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
/// End-to-end smoke: a mention observed by the reconciler drives the owning resolution actor to
/// persist a soft link on disk. Exercises the real InMemoryActorRuntime mailbox, not direct
/// Receive calls, so routing and idempotency flow through the runtime the way production would.
/// </summary>
[TestClass]
public sealed class ResolutionSubsystemIntegrationTests
{
    private string _projectRoot = "";

    [TestInitialize]
    public void Setup()
    {
        _projectRoot = Path.Combine(Path.GetTempPath(), "res-int-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_projectRoot, "people", "raha"));
        Directory.CreateDirectory(Path.Combine(_projectRoot, "people", "ryan"));
    }

    [TestCleanup]
    public void Teardown()
    {
        if (Directory.Exists(_projectRoot)) Directory.Delete(_projectRoot, true);
    }

    private EntityRecord MakeEntity(string key, string display, params string[] aliases)
    {
        var root = Path.Combine(_projectRoot, "people", key);
        Directory.CreateDirectory(root);
        return new EntityRecord
        {
            EntityKey = key,
            EntityType = "person",
            RootPath = root,
            Metadata = new EntityMetadata
            {
                EntityKey = key,
                EntityType = "person",
                DisplayName = display,
                Aliases = new List<string>(aliases)
            }
        };
    }

    [TestMethod]
    public async Task Mention_Drives_SoftLink_Persisted_To_Disk()
    {
        // Arrange runtime and supervisor.
        using var runtime = new InMemoryActorRuntime();
        await runtime.InitializeAsync(new Dictionary<string, object>());

        var entities = new List<EntityRecord>
        {
            MakeEntity("raha", "Raha", "Raha Mohebbi"),
            MakeEntity("ryan", "Ryan")
        };

        var policy = new ResolutionPolicy { Enabled = true };
        var producers = new List<ISignalProducer>
        {
            new AliasMatcher(),
            new SurfaceUniqueness(),
            new NegativeAssertions()
        };
        var addressing = new DefaultResolutionAddressing();

        var sup = await runtime.SpawnActorAsync(addressing.SupervisorIdFor("p1"),
            (id) => new ResolutionSupervisorActor(id, "p1", _projectRoot, runtime, policy, producers, addressing));
        await sup.SpawnAllAsync(entities);

        // Act: reconciler receives a mention of "Raha" while editing ryan's relationships.
        var mention = new MentionRef
        {
            MentionId = "scenario:s1:ryan#relationships.family[0]",
            Scope = ResolutionScope.Scenario("s1"),
            SurfaceForm = "Raha",
            WithinEntityKey = "ryan",
            SourcePath = "people/ryan/relationships.md",
            Field = "relationships.family[0]",
            SessionId = "sess-B",
            TurnId = "turn-2"
        };
        await runtime.SendMessageAsync(
            addressing.ReconcilerIdFor("p1"),
            new MentionObserved { Mention = mention },
            senderId: "test");

        // Allow the runtime mailbox to process both the reconciler and the resolution actor.
        await WaitForIncomingAsync(Path.Combine(_projectRoot, "people", "raha"), TimeSpan.FromSeconds(3));

        // Assert: raha's .resolution/incoming.yaml contains a soft edge for the mention.
        var store = new ResolutionEdgeStore(Path.Combine(_projectRoot, "people", "raha"));
        var doc = store.Load();
        Assert.AreEqual(1, doc.Edges.Count, "expected exactly one inbound edge on raha");
        var edge = doc.Edges[0];
        Assert.AreEqual("raha", edge.TargetEntityKey);
        Assert.AreEqual(ResolutionEdgeState.Soft, edge.State);
        Assert.IsTrue(edge.Confidence > 0.4, $"expected confidence >0.4, got {edge.Confidence}");
        Assert.AreEqual(mention.MentionId, edge.Mention.MentionId);

        await sup.ShutdownAllAsync();
    }

    private static async Task WaitForIncomingAsync(string entityRoot, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var path = ResolutionPaths.IncomingPath(entityRoot);
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path) && new FileInfo(path).Length > 0) return;
            await Task.Delay(50);
        }
    }
}
