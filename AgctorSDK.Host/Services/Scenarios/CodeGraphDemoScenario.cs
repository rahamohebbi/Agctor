using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Host.Models;
using AgctorSDK.CodeGraph.Actors;
using AgctorSDK.CodeGraph.Analyzers;
using AgctorSDK.CodeGraph.Analyzers.Roslyn;
using AgctorSDK.CodeGraph.Embeddings;
using AgctorSDK.CodeGraph.Agents;
using AgctorSDK.Core.Utils.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace AgctorSDK.Host.Services.Scenarios
{
    /// <summary>
    /// Demonstrates the CodeGraph subsystem in action by creating a minimal hierarchy (Solution → Project → File)
    /// and an <see cref="IndexerAgent"/> that walks the graph and stores embeddings in an in-memory vector store.
    /// </summary>
    public sealed class CodeGraphDemoScenario : IScenario
    {
        private readonly IActorRuntimeAdapter _runtimeAdapter;
        private readonly IAgentRegistry _agentRegistry;
        private readonly ILogger<CodeGraphDemoScenario> _logger;

        public string Name => "code-graph-demo";
        public string Description => "Creates a minimal CodeGraph with an IndexerAgent that indexes a sample C# file";

        public CodeGraphDemoScenario(
            IActorRuntimeAdapter runtimeAdapter,
            IAgentRegistry agentRegistry,
            ILogger<CodeGraphDemoScenario> logger)
        {
            _runtimeAdapter = runtimeAdapter;
            _agentRegistry = agentRegistry;
            _logger = logger;
        }

        public async Task<ScenarioSetupResponse> SetupAsync(Dictionary<string, object>? parameters = null)
        {
            try
            {
                // 1. Create a temporary workspace with a simple C# source file.
                var tempDir = Path.Combine(Path.GetTempPath(), $"agctor-demo-{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempDir);
                var srcFilePath = Path.Combine(tempDir, "Calculator.cs");
                await File.WriteAllTextAsync(srcFilePath, SampleSource);

                // 2. Build CodeGraph actors (Solution → Project → File)
                var solution = new SolutionActor("DemoSolution", Path.Combine(tempDir, "Demo.sln"));
                var project = new ProjectActor("DemoProject", Path.Combine(tempDir, "Demo.csproj"));
                var fileActor = new FileActor("Calculator.cs", srcFilePath);
                project.AddFile(fileActor);
                solution.AddProject(project);

                // 3. Prepare analyzer registry and embedding infrastructure.
                var registry = new AnalyzerRegistry();
                registry.RegisterAnalyzer(new RoslynCodeAnalyzer());

                var vectorStore = new InMemoryVectorStore();
                var storeActor = new EmbeddingStoreActor("vector-store", vectorStore);
                var embeddingGen = new StubEmbeddingGenerator();

                // 4. Spawn application agents.
                const string indexerId = "indexer-agent";
                const string searchId  = "search-agent";
                const string llmId     = "llm-agent";
                const string queryId   = "query-agent";

                var indexerAgent = new IndexerAgent(indexerId, registry, embeddingGen, storeActor);
                indexerAgent.Configure(registry, embeddingGen, storeActor, solution);

                var searchAgent  = new SearchAgent(searchId, embeddingGen, storeActor, solution);

                var llmAgent     = new LLMAgent(llmId); // Uses default Ollama settings – OK for demo.

                // Manually initialize and then register the prebuilt agents
                foreach (var agent in new Agent[] { indexerAgent, searchAgent, llmAgent })
                {
                    await agent.InitializeAsync();
                    await _runtimeAdapter.RegisterActorAsync(agent);
                    await _agentRegistry.RegisterAgentAsync(agent);
                }

                // Spawn QueryAgent via runtime so AgentFactory gets injected automatically
                var spawnedQuery = await _runtimeAdapter.SpawnActorAsync<QueryAgent>(
                    queryId,
                    id => new QueryAgent(id, searchId, llmId));

                // Inject an AgentFactory so QueryAgent has access to the runtime for sub-messages.
                // A lightweight factory is sufficient for the demo (no DI container required).
                var consoleLogger = new AgctorConsoleLogger();
                var sp = new Microsoft.Extensions.DependencyInjection.ServiceCollection().BuildServiceProvider();
                var agentFactory = new AgctorSDK.Core.Agents.AgentFactory(_runtimeAdapter, sp, consoleLogger, _agentRegistry);
                spawnedQuery.SetAgentFactory(agentFactory);

                await _agentRegistry.RegisterAgentAsync(spawnedQuery);

                _logger.LogInformation("CodeGraph demo scenario set up – agents ready (Indexer, Search, LLM, Query)");

                var created = new List<string> { indexerId, searchId, llmId, queryId };
                var roles = new Dictionary<string, string>
                {
                    [indexerId] = "Indexes CodeGraph and stores embeddings",
                    [searchId]  = "Vector search over CodeGraph",
                    [llmId]     = "Large-language-model interface (Ollama)",
                    [queryId]   = "Orchestrator – user-facing agent"
                };

                return new ScenarioSetupResponse(true, Name, created, roles, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set up CodeGraph demo scenario");
                return new ScenarioSetupResponse(false, Name, new List<string>(), new Dictionary<string, string>(), ex.Message);
            }
        }

        /// <summary>
        /// Very small sample source used for the demo graph.
        /// </summary>
        private const string SampleSource = @"namespace DemoApp
{
    public class Calculator
    {
        public int Add(int a, int b) => a + b;
    }
}";

        /// <summary>
        /// Lightweight deterministic embedding generator used by the demo.
        /// </summary>
        private sealed class StubEmbeddingGenerator : IEmbeddingGenerator
        {
            public Task<float[]> GenerateEmbeddingAsync(string text)
            {
                var hash = text.GetHashCode();
                float[] vec =
                {
                    ((hash >> 0) & 0xFF) / 255f,
                    ((hash >> 8) & 0xFF) / 255f,
                    ((hash >> 16) & 0xFF) / 255f
                };
                return Task.FromResult(vec);
            }
        }
    }
} 