using System.Collections.Generic;
using AgctorSDK.Core.ProjectMemory.Resolution.Actors;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.ProjectMemory.Resolution;

[TestClass]
public sealed class MentionIndexActorTests
{
    private static MentionIndexActor BuildIndex()
    {
        var entities = new[]
        {
            new MentionIndexActor.IndexedEntity
            {
                EntityKey = "raha",
                EntityPath = "/tmp/raha",
                DisplayName = "Raha",
                Aliases = new[] { "Raha Mohebbi" }
            },
            new MentionIndexActor.IndexedEntity
            {
                EntityKey = "ryan",
                EntityPath = "/tmp/ryan",
                DisplayName = "Ryan",
                Aliases = new List<string>()
            }
        };
        return new MentionIndexActor("midx:test", entities);
    }

    [TestMethod]
    public void Finds_Unique_Candidate_For_Rare_Name()
    {
        var idx = BuildIndex();
        var r = idx.Lookup("Raha");
        Assert.AreEqual(1, r.Candidates.Count);
        Assert.AreEqual("raha", r.Candidates[0].EntityKey);
        Assert.AreEqual(1.0, r.Candidates[0].UniquenessScore);
    }

    [TestMethod]
    public void Case_And_Diacritics_Insensitive()
    {
        var idx = BuildIndex();
        var r = idx.Lookup("RÄHÄ");
        Assert.AreEqual(1, r.Candidates.Count);
    }

    [TestMethod]
    public void Unknown_Surface_Returns_Empty()
    {
        var idx = BuildIndex();
        var r = idx.Lookup("Sarah");
        Assert.AreEqual(0, r.Candidates.Count);
    }

    [TestMethod]
    public void Collision_Spreads_Uniqueness()
    {
        var entities = new[]
        {
            new MentionIndexActor.IndexedEntity { EntityKey = "raha1", DisplayName = "Raha" },
            new MentionIndexActor.IndexedEntity { EntityKey = "raha2", DisplayName = "Raha" }
        };
        var idx = new MentionIndexActor("midx:test", entities);
        var r = idx.Lookup("Raha");
        Assert.AreEqual(2, r.Candidates.Count);
        Assert.AreEqual(0.5, r.Candidates[0].UniquenessScore);
    }
}
