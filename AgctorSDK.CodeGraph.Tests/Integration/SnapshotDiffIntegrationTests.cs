using System;
using System.IO;
using System.Threading.Tasks;
using AgctorSDK.CodeGraph.Actors;
using AgctorSDK.CodeGraph.Analyzers;
using AgctorSDK.CodeGraph.Analyzers.Roslyn;
using AgctorSDK.CodeGraph.Agents;
using AgctorSDK.CodeGraph.Messages;
using AgctorSDK.CodeGraph.Snapshots;
using AgctorSDK.Core.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.CodeGraph.Tests.Integration
{
    /// <summary>
    /// Group-4 integration tests – validates the end-to-end snapshot + diff pipeline
    /// driven by <see cref="GitWatcherAgent"/>.
    /// </summary>
    [TestClass]
    public class SnapshotDiffIntegrationTests
    {
        [TestMethod]
        public async Task GitWatcherAgent_ShouldCreateSnapshots_And_DiffDetectsChanges()
        {
            // Arrange – temp repo directory
            var repoPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(repoPath);

            var registry = new AnalyzerRegistry();
            registry.RegisterAnalyzer(new RoslynCodeAnalyzer());

            // commit1 – original graph with single method
            var graphV1 = BuildGraph(new[] { "Foo" });
            var watcher1 = new GitWatcherAgent("watcher1", repoPath, graphV1, registry);
            var envelope1 = new MessageEnvelope(new CreateSnapshotMessage("commit1"));
            var response1 = await watcher1.ReceiveAsync(envelope1);
            var created1 = (SnapshotCreatedMessage)response1.Payload;
            Assert.IsTrue(File.Exists(created1.Path), "First snapshot file should have been written to disk.");

            // commit2 – modified graph adds a new method
            var graphV2 = BuildGraph(new[] { "Foo", "Bar" });
            var watcher2 = new GitWatcherAgent("watcher2", repoPath, graphV2, registry);
            var envelope2 = new MessageEnvelope(new CreateSnapshotMessage("commit2"));
            var response2 = await watcher2.ReceiveAsync(envelope2);
            var created2 = (SnapshotCreatedMessage)response2.Payload;
            Assert.IsTrue(File.Exists(created2.Path), "Second snapshot file should have been written to disk.");

            // Act – load snapshots and diff
            var snap1 = await SnapshotService.LoadSnapshotAsync(created1.Path);
            var snap2 = await SnapshotService.LoadSnapshotAsync(created2.Path);
            var diff = SnapshotDiffService.Diff(snap1, snap2, registry);

            // Assert – new method detected
            CollectionAssert.Contains(diff.AddedMethods, "MyService.Bar");
            // No removals expected
            Assert.AreEqual(0, diff.RemovedMethods.Count);
            Assert.AreEqual(0, diff.RemovedClasses.Count);
        }

        private static SolutionActor BuildGraph(string[] methodNames)
        {
            var solution = new SolutionActor("Sol", "sol.sln");
            var project = new ProjectActor("Proj", "proj.csproj");
            var file = new FileActor("MyService.cs", "MyService.cs");
            var cls = new ClassActor("MyService");
            foreach (var m in methodNames)
            {
                cls.AddMethod(new MethodActor(m));
            }
            file.AddClass(cls);
            project.AddFile(file);
            solution.AddProject(project);
            return solution;
        }
    }
} 