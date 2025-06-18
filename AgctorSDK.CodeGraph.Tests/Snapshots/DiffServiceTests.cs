using System.Threading.Tasks;
using AgctorSDK.CodeGraph.Actors;
using AgctorSDK.CodeGraph.Analyzers;
using AgctorSDK.CodeGraph.Analyzers.Roslyn;
using AgctorSDK.CodeGraph.Snapshots;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.CodeGraph.Tests.Snapshots
{
    [TestClass]
    public class DiffServiceTests
    {
        [TestMethod]
        public void DiffService_DetectsAddedMethod()
        {
            var registry = new AnalyzerRegistry();
            registry.RegisterAnalyzer(new RoslynCodeAnalyzer());

            var oldRoot = BuildGraph(new[] { "Bar" });
            var newRoot = BuildGraph(new[] { "Bar", "Baz" });

            var diff = SnapshotDiffService.Diff(oldRoot, newRoot, registry);
            CollectionAssert.Contains(diff.AddedMethods, "Foo.Baz");
        }

        private static SolutionActor BuildGraph(string[] methodNames)
        {
            var sol = new SolutionActor("Sol", "s");
            var proj = new ProjectActor("Proj", "p");
            var file = new FileActor("Foo.cs", "Foo.cs");
            var cls = new ClassActor("Foo");
            foreach (var m in methodNames) cls.AddMethod(new MethodActor(m));
            file.AddClass(cls);
            proj.AddFile(file);
            sol.AddProject(proj);
            return sol;
        }
    }
} 