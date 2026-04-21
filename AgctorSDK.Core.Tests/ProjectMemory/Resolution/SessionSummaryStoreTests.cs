using System;
using System.Collections.Generic;
using System.IO;
using AgctorSDK.Core.ProjectMemory.Resolution.Messages;
using AgctorSDK.Core.ProjectMemory.Resolution.Models;
using AgctorSDK.Core.ProjectMemory.Resolution.Persistence;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.ProjectMemory.Resolution;

[TestClass]
public sealed class SessionSummaryStoreTests
{
    private string _root = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "ss-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Teardown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [TestMethod]
    public void Save_Then_Load_Round_Trips()
    {
        var store = new SessionSummaryStore(_root);
        var summary = new SessionSummary
        {
            SessionId = "sess-A",
            ProjectId = "p1",
            Mentions = new List<MentionRef>
            {
                new() { MentionId = "m1", SurfaceForm = "Raha", Scope = ResolutionScope.Project() }
            },
            AssertedFacts = new List<string> { "Raha lives in Ottawa" },
            NegativeAssertions = new List<string>()
        };
        store.Save(summary);

        var loaded = store.Load("sess-A");
        Assert.IsNotNull(loaded);
        Assert.AreEqual("sess-A", loaded!.SessionId);
        Assert.AreEqual(1, loaded.Mentions.Count);
        Assert.AreEqual("Raha", loaded.Mentions[0].SurfaceForm);
    }

    [TestMethod]
    public void Load_Missing_Returns_Null()
    {
        var store = new SessionSummaryStore(_root);
        Assert.IsNull(store.Load("nope"));
    }
}
