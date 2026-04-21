using System;
using System.IO;
using AgctorSDK.Core.ProjectMemory.Resolution.Models;
using AgctorSDK.Core.ProjectMemory.Resolution.Persistence;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.ProjectMemory.Resolution;

[TestClass]
public sealed class ResolutionEdgeStoreTests
{
    private string _tmp = "";

    [TestInitialize]
    public void Setup()
    {
        _tmp = Path.Combine(Path.GetTempPath(), "res-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmp);
    }

    [TestCleanup]
    public void Teardown()
    {
        if (Directory.Exists(_tmp)) Directory.Delete(_tmp, true);
    }

    private static ResolutionEdge Edge(string edgeId, string target, double conf = 0.5)
        => new ResolutionEdge
        {
            EdgeId = edgeId,
            TargetEntityKey = target,
            Mention = new MentionRef { SurfaceForm = "Raha", MentionId = "m1" },
            Confidence = conf
        };

    [TestMethod]
    public void Upsert_Creates_File_With_One_Edge()
    {
        var store = new ResolutionEdgeStore(_tmp);
        store.Upsert(Edge("e1", "raha"));

        Assert.IsTrue(File.Exists(ResolutionPaths.IncomingPath(_tmp)));
        var doc = store.Load();
        Assert.AreEqual(1, doc.Edges.Count);
        Assert.AreEqual("e1", doc.Edges[0].EdgeId);
    }

    [TestMethod]
    public void Upsert_Twice_With_Same_EdgeId_Does_Not_Duplicate()
    {
        var store = new ResolutionEdgeStore(_tmp);
        var a = Edge("e1", "raha", 0.4);
        a.Signals.Add(new ResolutionSignal { Kind = "aliasMatch", InputsFingerprint = "fp1", Score = 1.0, Weight = 0.25 });
        store.Upsert(a);

        var b = Edge("e1", "raha", 0.6);
        b.Signals.Add(new ResolutionSignal { Kind = "aliasMatch", InputsFingerprint = "fp1", Score = 1.0, Weight = 0.25 });     // same fingerprint, should not duplicate
        b.Signals.Add(new ResolutionSignal { Kind = "uniqueness", InputsFingerprint = "fp2", Score = 1.0, Weight = 0.20 });     // new
        store.Upsert(b);

        var doc = store.Load();
        Assert.AreEqual(1, doc.Edges.Count);
        Assert.AreEqual(2, doc.Edges[0].Signals.Count);
        Assert.AreEqual(0.6, doc.Edges[0].Confidence);
    }

    [TestMethod]
    public void AppendPromotion_Creates_Log_With_Yaml_Document_Separator()
    {
        var store = new ResolutionEdgeStore(_tmp);
        store.AppendPromotion("e1", new ResolutionPromotion
        {
            From = ResolutionEdgeState.Soft,
            To = ResolutionEdgeState.Hard,
            By = "auto",
            ConfidenceSnapshot = 0.91
        });
        store.AppendPromotion("e1", new ResolutionPromotion
        {
            From = ResolutionEdgeState.Hard,
            To = ResolutionEdgeState.Soft,
            By = "user:alice"
        });

        var path = ResolutionPaths.PromotionsPath(_tmp);
        Assert.IsTrue(File.Exists(path));
        var text = File.ReadAllText(path);
        StringAssert.Contains(text, "---");
        StringAssert.Contains(text, "auto");
        StringAssert.Contains(text, "user:alice");
    }
}
