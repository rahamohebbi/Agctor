using System.Collections.Generic;
using AgctorSDK.Core.ProjectMemory.Resolution.Messages;
using AgctorSDK.Core.ProjectMemory.Resolution.Models;
using AgctorSDK.Core.ProjectMemory.Resolution.Trace;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.ProjectMemory.Resolution;

[TestClass]
public sealed class ResolveSpanDetailTests
{
    [TestMethod]
    public void Build_Produces_Three_Sections()
    {
        var edge = new ResolutionEdge
        {
            EdgeId = "e1",
            TargetEntityKey = "raha",
            Mention = new MentionRef { SurfaceForm = "Raha" },
            State = ResolutionEdgeState.Soft,
            Confidence = 0.72,
            Signals = new List<ResolutionSignal>
            {
                new() { Kind = "aliasMatch", Score = 1.0, Weight = 0.25, Rationale = "exact", ProducedBy = "AliasMatcher@1" },
                new() { Kind = "uniqueness", Score = 1.0, Weight = 0.20, ProducedBy = "SurfaceUniqueness@1" }
            }
        };
        var request = new ResolveCandidate { Mention = edge.Mention, CandidateEntityKey = "raha" };
        var allCands = new[] { new LookupCandidate { EntityKey = "raha", SurfaceScore = 1.0, UniquenessScore = 1.0 } };

        var dto = ResolveSpanDetail.Build(request, edge, allCands);
        Assert.AreEqual("Raha", dto.Input.SurfaceForm);
        Assert.AreEqual(1, dto.Input.AllCandidates.Count);
        Assert.AreEqual(2, dto.Evidence.Signals.Count);
        Assert.AreEqual("Soft", dto.Outcome.State);
        Assert.AreEqual("e1", dto.Outcome.EdgeId);
    }
}
