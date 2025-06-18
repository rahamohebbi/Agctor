using System.Linq;
using System.Threading.Tasks;
using AgctorSDK.CodeGraph.Analyzers;
using AgctorSDK.CodeGraph.Analyzers.Roslyn;
using AgctorSDK.CodeGraph.Analyzers.Stubs;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.CodeGraph.Tests.Analyzers
{
    [TestClass]
    public class AnalyzerRegistryTests
    {
        private AnalyzerRegistry _registry = null!;

        [TestInitialize]
        public void Setup()
        {
            _registry = new AnalyzerRegistry();
            _registry.RegisterAnalyzer(new RoslynCodeAnalyzer());
            _registry.RegisterAnalyzer(new TreeSitterAnalyzer());
            _registry.RegisterAnalyzer(new LLMAnalyzer());
        }

        [TestMethod]
        public void Registry_ShouldReturnRegisteredAnalyzers_ByLanguage()
        {
            var analyzer = _registry.GetAnalyzerForLanguage("csharp");
            Assert.IsNotNull(analyzer);
            Assert.AreEqual("csharp", analyzer!.Language);
        }

        [TestMethod]
        public void Registry_ShouldReturnAnalyzer_ByExtension()
        {
            var analyzer = _registry.GetAnalyzerForExtension(".cs");
            Assert.IsNotNull(analyzer);
            Assert.AreEqual("csharp", analyzer!.Language);
        }

        [TestMethod]
        public void Registry_ShouldContainAllRegisteredLanguages()
        {
            CollectionAssert.AreEquivalent(new[] { "csharp", "python", "llm-fallback" }, _registry.RegisteredLanguages.ToList());
        }
    }
} 