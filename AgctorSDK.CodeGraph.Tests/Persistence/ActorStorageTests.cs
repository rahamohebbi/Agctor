using System.IO;
using System.Threading.Tasks;
using AgctorSDK.CodeGraph.Actors;
using AgctorSDK.CodeGraph.Persistence;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.CodeGraph.Tests.Persistence
{
    [TestClass]
    public class ActorStorageTests
    {
        private static SolutionActor BuildSampleHierarchy()
        {
            var solution = new SolutionActor("MySolution", "/path/sol.sln");
            var project = new ProjectActor("ProjectA", "/path/ProjectA.csproj");
            var file = new FileActor("Foo.cs", "/proj/Foo.cs");
            var cls = new ClassActor("Foo");
            var method = new MethodActor("Bar");
            cls.AddMethod(method);
            file.AddClass(cls);
            project.AddFile(file);
            solution.AddProject(project);
            return solution;
        }

        [TestMethod]
        public async Task SaveAndLoad_RoundTripMaintainsHierarchy()
        {
            var storage = new FileSystemActorStorage();
            var root = BuildSampleHierarchy();
            var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            await storage.SaveAsync(root, dir);

            var loaded = await storage.LoadAsync<SolutionActor>(dir, recursive: true);
            Assert.AreEqual(1, loaded.Children.Count);
            var proj = (ProjectActor)loaded.Children[0];
            var file = (FileActor)proj.Children[0];
            var cls = (ClassActor)file.Children[0];
            var mth = (MethodActor)cls.Children[0];
            Assert.AreEqual("Bar", mth.Name);
        }

        [TestMethod]
        public async Task Load_NonRecursive_ShouldHaveNoChildren()
        {
            var storage = new FileSystemActorStorage();
            var root = BuildSampleHierarchy();
            var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            await storage.SaveAsync(root, dir);

            var loaded = await storage.LoadAsync<SolutionActor>(dir, recursive: false);
            Assert.AreEqual(0, loaded.Children.Count);
        }

        [TestMethod]
        public async Task DeleteStore_RemovesDirectory()
        {
            var storage = new FileSystemActorStorage();
            var root = BuildSampleHierarchy();
            var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            await storage.SaveAsync(root, dir);
            Assert.IsTrue(Directory.Exists(dir));
            await storage.DeleteStoreAsync(dir);
            Assert.IsFalse(Directory.Exists(dir));
        }
    }
} 