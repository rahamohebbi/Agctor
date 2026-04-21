using System;
using System.IO;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Resolution.Bridge;
using AgctorSDK.Core.ProjectMemory.Resolution.Messages;
using AgctorSDK.Core.ProjectMemory.Resolution.Models;
using AgctorSDK.Core.ProjectMemory.Resolution.Persistence;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.ProjectMemory.Resolution;

[TestClass]
public sealed class SidecarIntentSinkTests
{
    private string _root = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "sink-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Teardown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [TestMethod]
    public async Task Writes_Outgoing_File_For_Known_Host()
    {
        var sink = new SidecarIntentSink(host => host == "ryan" ? _root : null);
        await sink.ApplyAsync(new IngestIntentDraft
        {
            EdgeId = "e1",
            Kind = IntentKind.SoftLink,
            Mention = new MentionRef { WithinEntityKey = "ryan", SurfaceForm = "Raha" },
            TargetEntityKey = "raha",
            Confidence = 0.72
        });

        var path = Path.Combine(_root, ResolutionPaths.ResolutionFolder, "outgoing.yaml");
        Assert.IsTrue(File.Exists(path));
        var text = File.ReadAllText(path);
        StringAssert.Contains(text, "raha");
        StringAssert.Contains(text, "SoftLink");
    }

    [TestMethod]
    public async Task Skip_Unknown_Host()
    {
        var sink = new SidecarIntentSink(_ => null);
        await sink.ApplyAsync(new IngestIntentDraft
        {
            EdgeId = "e1",
            Mention = new MentionRef { WithinEntityKey = "missing" }
        });
        Assert.IsFalse(Directory.Exists(Path.Combine(_root, ResolutionPaths.ResolutionFolder)));
    }

    [TestMethod]
    public async Task Second_Write_For_Same_Edge_And_Kind_Replaces_Row()
    {
        var sink = new SidecarIntentSink(_ => _root);
        await sink.ApplyAsync(new IngestIntentDraft { EdgeId = "e1", Kind = IntentKind.SoftLink,
            Mention = new MentionRef { WithinEntityKey = "ryan" }, Confidence = 0.5 });
        await sink.ApplyAsync(new IngestIntentDraft { EdgeId = "e1", Kind = IntentKind.SoftLink,
            Mention = new MentionRef { WithinEntityKey = "ryan" }, Confidence = 0.9 });

        var text = File.ReadAllText(Path.Combine(_root, ResolutionPaths.ResolutionFolder, "outgoing.yaml"));
        StringAssert.Contains(text, "0.9");
        Assert.IsFalse(text.Contains("confidence: 0.5"), "stale row should be replaced");
    }
}
