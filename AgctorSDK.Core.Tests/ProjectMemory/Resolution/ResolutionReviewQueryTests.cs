using System;
using System.Collections.Generic;
using System.IO;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Resolution.Models;
using AgctorSDK.Core.ProjectMemory.Resolution.Persistence;
using AgctorSDK.Core.ProjectMemory.Resolution.Review;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.ProjectMemory.Resolution;

[TestClass]
public sealed class ResolutionReviewQueryTests
{
    private string _root = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "review-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Teardown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private EntityRecord MakeEntity(string key)
    {
        var path = Path.Combine(_root, key);
        Directory.CreateDirectory(path);
        return new EntityRecord { EntityKey = key, EntityType = "person", RootPath = path, Metadata = new EntityMetadata { EntityKey = key, DisplayName = key } };
    }

    [TestMethod]
    public void Only_Soft_Edges_Returned_Sorted_By_Score_Times_Recency()
    {
        var a = MakeEntity("a");
        var b = MakeEntity("b");
        // Use a shared fixed timestamp so recency doesn't distort the ordering across a YAML
        // round-trip; confidence is what we want to assert on here.
        var ts = DateTimeOffset.UtcNow;

        new ResolutionEdgeStore(a.RootPath).Upsert(new ResolutionEdge
        {
            EdgeId = "e1", TargetEntityKey = "a",
            Mention = new MentionRef { SurfaceForm = "A" },
            State = ResolutionEdgeState.Soft, Confidence = 0.6,
            LastUpdatedAt = ts
        });
        new ResolutionEdgeStore(a.RootPath).Upsert(new ResolutionEdge
        {
            EdgeId = "e2", TargetEntityKey = "a",
            Mention = new MentionRef { SurfaceForm = "A-hard" },
            State = ResolutionEdgeState.Hard, Confidence = 0.95,
            LastUpdatedAt = ts
        });
        new ResolutionEdgeStore(b.RootPath).Upsert(new ResolutionEdge
        {
            EdgeId = "e3", TargetEntityKey = "b",
            Mention = new MentionRef { SurfaceForm = "B" },
            State = ResolutionEdgeState.Soft, Confidence = 0.85,
            LastUpdatedAt = ts
        });

        var q = new ResolutionReviewQuery(new List<EntityRecord> { a, b });
        var pending = q.Pending();
        Assert.AreEqual(2, pending.Count, "only soft edges");
        Assert.AreEqual("e3", pending[0].Edge.EdgeId, "higher confidence first");
    }

    [TestMethod]
    public void MinConfidence_Filters_Low_Edges()
    {
        var a = MakeEntity("a");
        new ResolutionEdgeStore(a.RootPath).Upsert(new ResolutionEdge
        {
            EdgeId = "low", TargetEntityKey = "a",
            Mention = new MentionRef { SurfaceForm = "A" },
            State = ResolutionEdgeState.Soft, Confidence = 0.3
        });

        var q = new ResolutionReviewQuery(new List<EntityRecord> { a });
        Assert.AreEqual(0, q.Pending(minConfidence: 0.5).Count);
    }
}
