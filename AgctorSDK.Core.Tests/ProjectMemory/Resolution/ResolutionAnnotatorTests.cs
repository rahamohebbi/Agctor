using System.Collections.Generic;
using System.IO;
using AgctorSDK.Core.ProjectMemory.Resolution.Models;
using AgctorSDK.Core.ProjectMemory.Resolution.Persistence;
using AgctorSDK.Core.ProjectMemory.Resolution.Review;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.ProjectMemory.Resolution;

/// <summary>
/// The annotator reads persisted edges and decorates free text with grade footnotes. Both soft
/// and hard edges need distinct markup so the UI and honest-narration hook can tell them apart.
/// </summary>
[TestClass]
public sealed class ResolutionAnnotatorTests
{
    private string _root = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "annot-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Teardown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private (string, string, string) Seed(string key, string display, ResolutionEdgeState state, double conf)
    {
        var entityRoot = Path.Combine(_root, "people", key);
        Directory.CreateDirectory(entityRoot);
        var store = new ResolutionEdgeStore(entityRoot);
        store.Upsert(new ResolutionEdge
        {
            EdgeId = $"m->{key}",
            TargetEntityKey = key,
            Mention = new MentionRef { MentionId = "m", SurfaceForm = display },
            State = state,
            Confidence = conf
        });
        return (key, display, entityRoot);
    }

    [TestMethod]
    public void AnnotateInline_SoftLink_Adds_SoftLinked_Footnote()
    {
        var e = Seed("raha", "Raha", ResolutionEdgeState.Soft, 0.72);
        var annotator = ResolutionAnnotator.FromEntities(new[] { e });

        var annotated = annotator.AnnotateInline("Ryan's father is Raha.");

        StringAssert.Contains(annotated, "soft-linked");
        StringAssert.Contains(annotated, "raha");
        StringAssert.Contains(annotated, "72%");
    }

    [TestMethod]
    public void AnnotateInline_HardLink_Uses_Arrow_Footnote()
    {
        var e = Seed("raha", "Raha", ResolutionEdgeState.Hard, 0.95);
        var annotator = ResolutionAnnotator.FromEntities(new[] { e });

        var annotated = annotator.AnnotateInline("Ryan's father is Raha.");

        StringAssert.Contains(annotated, "→ raha");
        Assert.IsFalse(annotated.Contains("soft-linked"));
    }

    [TestMethod]
    public void AnnotateInline_Leaves_Unrelated_Text_Alone()
    {
        var annotator = ResolutionAnnotator.FromEntities(new List<(string, string, string)>());
        Assert.AreEqual("Hello world.", annotator.AnnotateInline("Hello world."));
    }
}
