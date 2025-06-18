using AgctorSDK.CodeGraph.Analyzers;
using AgctorSDK.CodeGraph.Llm;
using AgctorSDK.CodeGraph.Analyzers.Stubs;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.CodeGraph.Tests.Analyzers
{
    [TestClass]
    public class AnalyzerRegistryFallbackTests
    {
        [TestMethod]
        public void Registry_ShouldReturnFallback_WhenNoStaticAnalyzer()
        {
            var registry = new AnalyzerRegistry();
            var stubLlm = new StubLlmClient("{\"classes\": []}");
            registry.EnableLlmFallback(stubLlm);

            var analyzer = registry.GetAnalyzerForExtension(".unknown");
            Assert.IsNotNull(analyzer);
            Assert.AreEqual("llm-fallback", analyzer.Language);
        }

        private class StubLlmClient : ILlmClient
        {
            private readonly string _resp;
            public StubLlmClient(string resp) => _resp = resp;
            public Task<string> CompleteAsync(string prompt, LlmOptions? options = null) => Task.FromResult(_resp);
        }
    }
} 