using System.IO;
using System.Threading.Tasks;
using AgctorSDK.CodeGraph.Actors;
using AgctorSDK.CodeGraph.Snapshots;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.CodeGraph.Tests.Snapshots
{
    [TestClass]
    public class SnapshotServiceTests
    {
        [TestMethod]
        public async Task Snapshot_RoundTrip_PreservesHierarchy()
        {
            var solution = new SolutionActor("Sol", "sol.sln");
            var proj = new ProjectActor("Proj", "p.csproj");
            var file = new FileActor("Foo.cs", "Foo.cs");
            file.AddClass(new ClassActor("Foo"));
            proj.AddFile(file);
            solution.AddProject(proj);

            var tempDir = Path.GetTempPath();
            var path = await SnapshotService.SaveSnapshotAsync(solution, tempDir, "snap1");
            var loaded = await SnapshotService.LoadSnapshotAsync(path);

            Assert.AreEqual(1, loaded.Children.Count);
            Assert.AreEqual("Proj", loaded.Children[0].Name);
        }
    }
} 