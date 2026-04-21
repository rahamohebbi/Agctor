using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Resolution.Bridge;
using AgctorSDK.Core.ProjectMemory.Resolution.Messages;
using AgctorSDK.Core.ProjectMemory.Resolution.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.ProjectMemory.Resolution;

/// <summary>
/// The MemoryIntentBridgeSink should write one JSON proposal per draft under
/// <c>.agctor/runtime/resolution/intents/</c>. Tests lock the file layout and the knowledge-type
/// mapping that the future ingest runner relies on.
/// </summary>
[TestClass]
public sealed class MemoryIntentBridgeSinkTests
{
    [TestMethod]
    public async Task ApplyAsync_Writes_One_File_Per_Draft_With_SoftLink_Mapping()
    {
        var root = Path.Combine(Path.GetTempPath(), "mem-bridge-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var sink = new MemoryIntentBridgeSink(root);
            var draft = new IngestIntentDraft
            {
                EdgeId = "m1->entity:raha",
                Kind = IntentKind.SoftLink,
                Mention = new MentionRef { MentionId = "m1", SurfaceForm = "Raha", WithinEntityKey = "ryan", Field = "relationships.family[0]" },
                TargetEntityKey = "raha",
                TargetEntityPath = Path.Combine(root, "people", "raha"),
                Confidence = 0.72
            };

            await sink.ApplyAsync(draft);

            var files = Directory.EnumerateFiles(sink.Folder, "*.json").ToList();
            Assert.AreEqual(1, files.Count);
            var content = File.ReadAllText(files[0]);
            StringAssert.Contains(content, "\"resolution-proposal\"");
            StringAssert.Contains(content, "\"softLinkTo\""); // soft link maps to softLinkTo knowledge type
            StringAssert.Contains(content, "\"raha\"");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task ApplyAsync_HardLink_Maps_To_EntityRef()
    {
        var root = Path.Combine(Path.GetTempPath(), "mem-bridge-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var sink = new MemoryIntentBridgeSink(root);
            await sink.ApplyAsync(new IngestIntentDraft
            {
                EdgeId = "e1",
                Kind = IntentKind.HardLink,
                Mention = new MentionRef { MentionId = "m1", SurfaceForm = "Raha", WithinEntityKey = "ryan" },
                TargetEntityKey = "raha",
                Confidence = 0.95
            });

            var content = File.ReadAllText(Directory.EnumerateFiles(sink.Folder, "*.json").Single());
            StringAssert.Contains(content, "\"entityRef\"");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
