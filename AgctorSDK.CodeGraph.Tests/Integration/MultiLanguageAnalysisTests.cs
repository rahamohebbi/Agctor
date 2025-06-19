using System.Collections.Generic;
using System.Threading.Tasks;
using AgctorSDK.CodeGraph.Analyzers;
using AgctorSDK.CodeGraph.Analyzers.Roslyn;
using AgctorSDK.CodeGraph.Analyzers.Stubs;
using AgctorSDK.CodeGraph.Actors;
using AgctorSDK.CodeGraph.Llm;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.CodeGraph.Tests.Integration
{
    public class StubLlmClient : ILlmClient
    {
        private readonly string _response;
        public StubLlmClient(string response) => _response = response;
        public Task<string> CompleteAsync(string prompt, LlmOptions? options = null) => Task.FromResult(_response);
    }

    [TestClass]
    public class MultiLanguageAnalysisTests
    {
        private AnalyzerRegistry _registry = null!;

        [TestInitialize]
        public void Setup()
        {
            _registry = new AnalyzerRegistry();
            _registry.RegisterAnalyzer(new RoslynCodeAnalyzer());
            _registry.RegisterAnalyzer(new TreeSitterAnalyzer());
            // Fallback LLM analyzer – returns a fixed JSON structure so parsing always succeeds
            var json = "{\"classes\":[{\"name\":\"Dummy\",\"methods\":[{\"name\":\"foo\"}]}]}";
            _registry.EnableLlmFallback(new StubLlmClient(json));
        }

        [TestMethod]
        public async Task AnalyzeMixedLanguageProject_ShouldUseCorrectAnalyzers()
        {
            // Arrange – build file actors with different extensions and simple code.
            var csFile = new FileActor("Foo.cs", "Foo.cs");
            var csCode = "public class Foo { void Bar(){} }";

            var pyFile = new FileActor("script.py", "script.py");
            var pyCode = "class StubClass:\n    def stub_method(self):\n        pass";

            var rsFile = new FileActor("lib.rs", "lib.rs");
            var rsCode = "fn main() {}";

            var fooFile = new FileActor("unknown.foo", "unknown.foo");
            var fooCode = "some unknown language content";

            var testCases = new List<(FileActor file,string code,System.Type expectedAnalyzer)>
            {
                (csFile, csCode, typeof(RoslynCodeAnalyzer)),
                (pyFile, pyCode, typeof(TreeSitterAnalyzer)),
                (rsFile, rsCode, typeof(LLMAnalyzer)),
                (fooFile, fooCode, typeof(LLMAnalyzer))
            };

            // Act & Assert
            foreach (var (file, code, expected) in testCases)
            {
                var analyzer = _registry.GetAnalyzerForExtension(System.IO.Path.GetExtension(file.PhysicalPath!).ToLowerInvariant());
                Assert.IsNotNull(analyzer, $"Analyzer should not be null for {file.Name}");
                Assert.IsInstanceOfType(analyzer, expected, $"File {file.Name} should use {expected.Name}");

                var parsed = await file.AnalyzeAsync(_registry, code);
                Assert.IsTrue(parsed.Classes.Count > 0, $"Parsed result for {file.Name} should contain classes");
            }
        }
    }
} 