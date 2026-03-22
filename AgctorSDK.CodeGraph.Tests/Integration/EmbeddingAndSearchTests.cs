using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AgctorSDK.CodeGraph.Actors;
using AgctorSDK.CodeGraph.Analyzers;
using AgctorSDK.CodeGraph.Analyzers.Roslyn;
using AgctorSDK.CodeGraph.Embeddings;
using AgctorSDK.CodeGraph.Agents;
using AgctorSDK.CodeGraph.Intents;
using AgctorSDK.Core.Adapters;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Registry;
using AgctorSDK.Core.Utils.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.DependencyInjection;

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

        private SolutionActor BuildSearchableSolution(string tempDir)
        {
            Directory.CreateDirectory(tempDir);
            var authPath = Path.Combine(tempDir, "Auth.cs");
            var utilPath = Path.Combine(tempDir, "Util.cs");

            File.WriteAllText(authPath, @"namespace Demo
{
    public class AuthService
    {
        public void Login() { }
    }
}");

            File.WriteAllText(utilPath, @"namespace Demo
{
    public class StringUtil
    {
        public string Trim(string input) => input.Trim();
    }
}");

            var solution = new SolutionActor("TestSolution", Path.Combine(tempDir, "Test.sln"));
            var proj = new ProjectActor("Core", Path.Combine(tempDir, "Core.csproj"));
            solution.AddProject(proj);

            var fileAuth = new FileActor("Auth.cs", authPath);
            proj.AddFile(fileAuth);
            var classAuth = new ClassActor("AuthService");
            fileAuth.AddClass(classAuth);
            classAuth.AddMethod(new MethodActor("Login"));

            var fileUtil = new FileActor("Util.cs", utilPath);
            proj.AddFile(fileUtil);
            var classUtil = new ClassActor("StringUtil");
            fileUtil.AddClass(classUtil);
            classUtil.AddMethod(new MethodActor("Trim"));

            return solution;
        }

        [TestMethod]
        public async Task IndexerAgent_ShouldGenerateAndStoreEmbeddings()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"embedding-search-{System.Guid.NewGuid():N}");
            var solution = BuildSearchableSolution(tempDir);
            var indexer = new IndexerAgent("idx", _registry, _generator, _storeActor);
            await indexer.IndexAsync(solution);
            int count = await _store.CountAsync();
            // Expect 2 classes + 2 methods = 4 embeddings
            Assert.AreEqual(4, count);
        }

        [TestMethod]
        public async Task Indexer_ShouldDiscoverNewMarkdownFile_OnDisk_WithoutExtraEmbeddings()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"embedding-sync-{Guid.NewGuid():N}");
            var solution = BuildSearchableSolution(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "NEW.md"), "# Title\n\nBody.");

            var indexer = new IndexerAgent("idx", _registry, _generator, _storeActor);
            await indexer.IndexAsync(solution);

            var fileNames = solution.Children
                .SelectMany(p => p.Children)
                .OfType<FileActor>()
                .Select(f => f.Name)
                .ToList();
            CollectionAssert.Contains(fileNames, "NEW.md");

            // Only .cs files produce class/method embeddings (Roslyn); .md is tree-only.
            int count = await _store.CountAsync();
            Assert.AreEqual(4, count, "Expected 2 classes + 2 methods from .cs files only.");
        }

        [TestMethod]
        public async Task VectorSearchActor_ShouldReturnSemanticMatches()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"embedding-search-{Guid.NewGuid():N}");
            var solution = BuildSearchableSolution(tempDir);
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

        [TestMethod]
        public async Task SearchAgent_ShouldAutoIndex_WhenSemanticQueryNeedsEmbeddings()
        {
            var runtime = new InMemoryActorRuntime();
            await runtime.InitializeAsync(new Dictionary<string, object>());

            var registry = new InMemoryAgentRegistry();
            var services = new ServiceCollection().BuildServiceProvider();
            var factory = new AgentFactory(runtime, services, new AgctorConsoleLogger(), registry);

            var tempDir = Path.Combine(Path.GetTempPath(), $"embedding-search-{Guid.NewGuid():N}");
            var solution = BuildSearchableSolution(tempDir);
            var indexer = new IndexerAgent("idx", _registry, _generator, _storeActor);
            indexer.Configure(_registry, _generator, _storeActor, solution);
            indexer.SetAgentFactory(factory);

            var coordinator = new EmbeddingCoordinatorAgent("embedding-coordinator-agent", "idx");
            coordinator.SetAgentFactory(factory);

            var search = new SearchAgent(
                "search-agent",
                _generator,
                _storeActor,
                solution,
                Array.Empty<IIntentResolver>(),
                "embedding-coordinator-agent");
            search.SetAgentFactory(factory);

            foreach (var agent in new Agent[] { indexer, coordinator, search })
            {
                await agent.InitializeAsync();
                await runtime.RegisterActorAsync(agent);
                await registry.RegisterAgentAsync(agent);
            }

            Assert.AreEqual(0, await _store.CountAsync(), "Store should start empty before semantic search.");

            var result = await runtime.SendMessageAsync<string>(
                "search-agent",
                "Login",
                TimeSpan.FromSeconds(10),
                senderId: "test",
                headers: new Dictionary<string, string> { ["MessageType"] = "Prompt" });

            Assert.AreEqual(4, await _store.CountAsync(), "Semantic search should auto-populate embeddings.");
            StringAssert.Contains(result, "Method: Login");
        }
    }
} 