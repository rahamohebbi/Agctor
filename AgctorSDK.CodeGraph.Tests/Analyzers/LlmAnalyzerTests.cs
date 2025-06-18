using System.Threading.Tasks;
using AgctorSDK.CodeGraph.Analyzers.Stubs;
using AgctorSDK.CodeGraph.Llm;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.CodeGraph.Tests.Analyzers
{
    public class StubLlmClient : ILlmClient
    {
        private readonly string _response;
        public StubLlmClient(string response) => _response = response;
        public Task<string> CompleteAsync(string prompt, LlmOptions? options = null) => Task.FromResult(_response);
    }

    [TestClass]
    public class LlmAnalyzerTests
    {
        private const string PythonSample = "class Foo:\n    def bar(self):\n        pass";

        [TestMethod]
        public async Task LlmAnalyzer_ShouldParseClassesAndMethods()
        {
            var jsonResponse = "{\"classes\": [{ \"name\": \"Foo\", \"methods\": [{\"name\": \"bar\"}]}]}";
            var analyzer = new LLMAnalyzer(new StubLlmClient(jsonResponse));
            var parsed = await analyzer.AnalyzeAsync("Foo.py", PythonSample);
            Assert.AreEqual(1, parsed.Classes.Count);
            Assert.AreEqual("Foo", parsed.Classes[0].Name);
            Assert.AreEqual("bar", parsed.Classes[0].Methods[0].Name);
        }
    }
} 