using System;
using System.Collections.Generic;
using System.IO;
using AgctorSDK.Core.ProjectMemory.Resolution.Models;
using AgctorSDK.Core.ProjectMemory.Resolution.Signals;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.ProjectMemory.Resolution;

[TestClass]
public sealed class AttributeOverlapTests
{
    private string _root = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "attr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Teardown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private SignalContext Ctx(IReadOnlyList<string> facts)
    {
        File.WriteAllText(Path.Combine(_root, "profile.md"),
            "Raha lives in Ottawa and works at Shopify as a software engineer.\n");
        File.WriteAllText(Path.Combine(_root, "timeline.md"),
            "2020 moved to Ottawa. 2021 joined Shopify.\n");
        return new SignalContext
        {
            CandidateEntityKey = "raha",
            CandidateEntityPath = _root,
            SessionAssertedFacts = facts
        };
    }

    [TestMethod]
    public void Score_Increases_With_Overlapping_Tokens()
    {
        var s = new AttributeOverlap().Score(Ctx(new[] { "Raha works at Shopify in Ottawa" }), new ResolutionPolicy());
        Assert.IsNotNull(s);
        Assert.AreEqual("attrOverlap", s!.Kind);
        Assert.IsTrue(s.Score > 0, $"expected >0, got {s.Score}");
    }

    [TestMethod]
    public void Returns_Null_When_No_Facts()
    {
        var s = new AttributeOverlap().Score(Ctx(Array.Empty<string>()), new ResolutionPolicy());
        Assert.IsNull(s);
    }

    [TestMethod]
    public void Returns_Null_When_No_Files()
    {
        var empty = Path.Combine(Path.GetTempPath(), "empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        try
        {
            var s = new AttributeOverlap().Score(new SignalContext
            {
                CandidateEntityKey = "x",
                CandidateEntityPath = empty,
                SessionAssertedFacts = new[] { "anything" }
            }, new ResolutionPolicy());
            Assert.IsNull(s);
        }
        finally { Directory.Delete(empty, true); }
    }
}
