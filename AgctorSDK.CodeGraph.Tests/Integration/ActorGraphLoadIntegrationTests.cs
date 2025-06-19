using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AgctorSDK.CodeGraph.Actors;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.CodeGraph.Tests.Integration
{
    /// <summary>
    /// Integration tests for Group-1: ensure that a persisted actor graph can be loaded back 1-for-1.
    /// </summary>
    [TestClass]
    public class ActorGraphLoadIntegrationTests
    {
        [TestMethod]
        public async Task LoadSolutionFromDisk_ShouldCreateCompleteActorGraph()
        {
            // Build an in-memory sample graph with multiple projects / files to exercise the loader.
            var solution = new SolutionActor("SampleSolution", "/repo/SampleSolution.sln");

            for (int p = 1; p <= 2; p++)
            {
                var project = new ProjectActor($"Project{p}", $"/repo/Project{p}/Project{p}.csproj");
                solution.AddProject(project);

                for (int f = 1; f <= 2; f++)
                {
                    var file = new FileActor($"File{f}.cs", $"/repo/Project{p}/File{f}.cs");
                    project.AddFile(file);

                    var @class = new ClassActor($"Class{p}{f}");
                    file.AddClass(@class);

                    var method = new MethodActor("Execute");
                    @class.AddMethod(method);
                }
            }

            // Persist to a temp directory mimicking .agctorstore structure.
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            await solution.SaveStateAsync(tempDir);

            // Act – load the solution back from disk.
            var loaded = await SolutionActor.LoadStateAsync(tempDir);

            // Gather counts via DFS traversal.
            var allActors = new List<CodeGraphActorBase>();
            Traverse(loaded, allActors);

            int projectCount = allActors.Count(a => a is ProjectActor);
            int fileCount    = allActors.Count(a => a is FileActor);
            int classCount   = allActors.Count(a => a is ClassActor);
            int methodCount  = allActors.Count(a => a is MethodActor);

            // Assertions – structure intact and counts match what we built.
            Assert.AreEqual(2, projectCount, "Should load 2 ProjectActor nodes");
            Assert.AreEqual(4, fileCount,    "Should load 4 FileActor nodes");
            Assert.AreEqual(4, classCount,   "Should load 4 ClassActor nodes");
            Assert.AreEqual(4, methodCount,  "Should load 4 MethodActor nodes");

            // Validate parent-child relationships for a sample branch (Project1/File1).
            var project1 = (ProjectActor)loaded.Children.Single(c => c.Name == "Project1");
            var file11   = (FileActor)project1.Children.Single(c => c.Name == "File1.cs");
            var class11  = (ClassActor)file11.Children.Single();
            var method11 = (MethodActor)class11.Children.Single();

            Assert.AreEqual("Execute", method11.Name);
            Assert.AreSame(class11, file11.Children[0]);
            Assert.AreSame(file11, project1.Children.First());

            // Clean-up temp data.
            try { Directory.Delete(tempDir, recursive: true); } catch { /* ignore */ }
        }

        private static void Traverse(CodeGraphActorBase actor, ICollection<CodeGraphActorBase> acc)
        {
            acc.Add(actor);
            foreach (var child in actor.Children)
            {
                Traverse(child, acc);
            }
        }
    }
} 