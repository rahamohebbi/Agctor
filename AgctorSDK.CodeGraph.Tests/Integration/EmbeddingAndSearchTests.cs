using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AgctorSDK.CodeGraph.Actors;
using AgctorSDK.CodeGraph.Analyzers;
using AgctorSDK.CodeGraph.Analyzers.Roslyn;
using AgctorSDK.CodeGraph.Embeddings;
using AgctorSDK.CodeGraph.Agents;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.CodeGraph.Tests.Integration
{
    internal class StubEmbeddingGenerator : IEmbeddingGenerator
    {
        public Task<float[]> GenerateEmbeddingAsync(string text)
        {
            // Simple deterministic 3-dim vector based on string hash
            int h = text.GetHashCode();
            return Task.FromResult(new float[]
            {
                ((h >>  0) & 0xFF)/255f,
                ((h >>  8) & 0xFF)/255f,
                ((h >> 16) & 0xFF)/255f
            });
        }
    }

    [TestClass]
    public class EmbeddingAndSearchTests
    {
        private AnalyzerRegistry _registry = null!;
        private StubEmbeddingGenerator _generator = null!;
        private InMemoryVectorStore _store = null!;
        private EmbeddingStoreActor _storeActor = null!;

        [TestInitialize]
        public void Setup()
        {
            _registry = new AnalyzerRegistry();
            _registry.RegisterAnalyzer(new RoslynCodeAnalyzer());
            _generator = new StubEmbeddingGenerator();
            _store = new InMemoryVectorStore();
            _storeActor = new EmbeddingStoreActor("store", _store);
        }

        private SolutionActor BuildSampleSolution()
        {
            var solution = new SolutionActor("TestSolution", "/s/Test.sln");
            var proj = new ProjectActor("Core", "/s/Core.csproj");
            solution.AddProject(proj);

            // File with Auth class and Login method
            var fileAuth = new FileActor("Auth.cs", "Auth.cs");
            proj.AddFile(fileAuth);
            var classAuth = new ClassActor("AuthService");
            fileAuth.AddClass(classAuth);
            classAuth.AddMethod(new MethodActor("Login"));

            // File with unrelated util class
            var fileUtil = new FileActor("Util.cs", "Util.cs");
            proj.AddFile(fileUtil);
            var classUtil = new ClassActor("StringUtil");
            fileUtil.AddClass(classUtil);
            classUtil.AddMethod(new MethodActor("Trim"));
            return solution;
        }

        [TestMethod]
        public async Task IndexerAgent_ShouldGenerateAndStoreEmbeddings()
        {
            var solution = BuildSampleSolution();
            var indexer = new IndexerAgent("idx", _registry, _generator, _storeActor);
            await indexer.IndexAsync(solution);
            int count = await _store.CountAsync();
            // Expect 2 classes + 2 methods = 4 embeddings
            Assert.AreEqual(4, count);
        }

        [TestMethod]
        public async Task VectorSearchActor_ShouldReturnSemanticMatches()
        {
            var solution = BuildSampleSolution();
            var indexer = new IndexerAgent("idx", _registry, _generator, _storeActor);
            await indexer.IndexAsync(solution);

            // Query vector for word "Login"
            var queryVec = await _generator.GenerateEmbeddingAsync("Login");
            var queryMsg = new QueryEmbeddingMessage(queryVec, 3);
            var envelope = new AgctorSDK.Core.Messages.MessageEnvelope(queryMsg);
            var resultEnv = await _storeActor.ReceiveAsync(envelope);
            var res = (QueryResultMessage)resultEnv.Payload;
            Assert.IsTrue(res.Results.Any(), "Should return at least one result");

            // Resolve the expected actor id for the "Login" method within the solution hierarchy
            var loginMethodId = solution
                .Children              // projects
                .SelectMany(p => p.Children)
                .SelectMany(f => f.Children) // files -> classes
                .SelectMany(c => c.Children)
                .OfType<MethodActor>()
                .First(m => m.Name == "Login").Id;

            var top = res.Results.First();
            Assert.AreEqual(loginMethodId, top.ActorId, "Vector store should return the Login method actor id as the top match");
        }
    }
} 