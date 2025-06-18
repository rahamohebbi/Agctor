using System.Threading.Tasks;
using AgctorSDK.CodeGraph.Actors;
using AgctorSDK.CodeGraph.Analyzers;
using AgctorSDK.CodeGraph.Analyzers.Roslyn;
using AgctorSDK.CodeGraph.Analyzers.Stubs;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.CodeGraph.Tests.Actors
{
    [TestClass]
    public class FileActorAnalyzerIntegrationTests
    {
        private AnalyzerRegistry _registry = null!;

        [TestInitialize]
        public void Init()
        {
            _registry = new AnalyzerRegistry();
            _registry.RegisterAnalyzer(new RoslynCodeAnalyzer());
            _registry.RegisterAnalyzer(new LLMAnalyzer()); // fallback for .txt
        }

        [TestMethod]
        public async Task FileActor_ShouldUseRoslynAnalyzer_ForCsFiles()
        {
            const string code = "public class X { void Y() {} }";
            var fileActor = new FileActor("X.cs", "X.cs"); // Path not used as we pass source override

            var parsed = await fileActor.AnalyzeAsync(_registry, code);

            Assert.AreEqual(1, parsed.Classes.Count);
            Assert.AreEqual("X", parsed.Classes[0].Name);
        }

        [TestMethod]
        [ExpectedException(typeof(System.InvalidOperationException))]
        public async Task FileActor_ShouldThrow_WhenNoAnalyzerRegistered()
        {
            var emptyRegistry = new AnalyzerRegistry();
            var fileActor = new FileActor("data.xyz", "data.xyz");
            await fileActor.AnalyzeAsync(emptyRegistry, "dummy");
        }
    }
} 