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
using AgctorSDK.CodeGraph.Persistence;
using AgctorSDK.Core.Utils.Logging;
using Microsoft.Extensions.DependencyInjection;
using AgctorSDK.CodeGraph.Intents;
using AgctorSDK.CodeGraph.Llm;

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
        private readonly ICodeGraphContextAccessor _codeGraphContextAccessor;
        private readonly ILogger<CodeGraphDemoScenario> _logger;

        public string Name => "code-graph-demo";
        public string Description => "Creates a minimal CodeGraph with an IndexerAgent that indexes a sample C# file";

        public CodeGraphDemoScenario(
            IActorRuntimeAdapter runtimeAdapter,
            IAgentRegistry agentRegistry,
            ICodeGraphContextAccessor codeGraphContextAccessor,
            ILogger<CodeGraphDemoScenario> logger)
        {
            _runtimeAdapter = runtimeAdapter;
            _agentRegistry = agentRegistry;
            _codeGraphContextAccessor = codeGraphContextAccessor ?? throw new ArgumentNullException(nameof(codeGraphContextAccessor));
            _logger = logger;
        }

        public async Task<ScenarioSetupResponse> SetupAsync(Dictionary<string, object>? parameters = null)
        {
            try
            {
                // 1. Create a temporary workspace with a simple C# source file.
                var tempDir = Path.Combine(Path.GetTempPath(), $"agctor-demo-{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempDir);
                _logger.LogInformation("[CodeGraphDemoScenario] Workspace directory: {TempDir}", tempDir);
                Console.WriteLine($"[AGCTOR DEMO] Workspace directory: {tempDir}");
                var calcPath = Path.Combine(tempDir, "Calculator.cs");
                var utilsPath = Path.Combine(tempDir, "MathUtils.cs");
                var sciPath  = Path.Combine(tempDir, "ScientificCalculator.cs");

                await File.WriteAllTextAsync(calcPath, CalculatorSource);
                await File.WriteAllTextAsync(utilsPath, MathUtilsSource);
                await File.WriteAllTextAsync(sciPath, ScientificCalculatorSource);

                // 1b. Create a minimal xUnit test project so the demo pipeline's TestRunnerTool has tests to run.
                var testsProjPath = Path.Combine(tempDir, "AgctorSDK.Core.Tests.csproj");
                var testsFilePath = Path.Combine(tempDir, "MathUtilsTests.cs");

                var testsCsproj = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include=""Microsoft.NET.Test.Sdk"" Version=""17.9.0"" />
    <PackageReference Include=""xunit"" Version=""2.5.0"" />
    <PackageReference Include=""xunit.runner.visualstudio"" Version=""2.5.0"" />
    <PackageReference Include=""coverlet.collector"" Version=""6.0.0"" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include=""Demo.csproj"" />
  </ItemGroup>
</Project>";

                var testCode = @"using DemoApp;using Xunit;namespace DemoApp.Tests{public class MathUtilsTests{[Theory][InlineData(0,0)][InlineData(2,8)][InlineData(-3,-27)]public void Cube_Works(int input,int expected){Assert.Equal(expected,MathUtils.Cube(input));}}}";

                await File.WriteAllTextAsync(testsProjPath, testsCsproj);
                await File.WriteAllTextAsync(testsFilePath, testCode);

                // Make the demo workspace the current directory so tooling can resolve relative paths like "MathUtils.cs".
                Directory.SetCurrentDirectory(tempDir);

                // 2. Build CodeGraph actors (Solution → Project → File)
                var solution = new SolutionActor("DemoSolution", Path.Combine(tempDir, "Demo.sln"));
                var project = new ProjectActor("DemoProject", Path.Combine(tempDir, "Demo.csproj"));
                var calcFile = new FileActor("Calculator.cs", calcPath);
                var utilsFile = new FileActor("MathUtils.cs", utilsPath);
                var sciFile  = new FileActor("ScientificCalculator.cs", sciPath);
                project.AddFile(calcFile);
                project.AddFile(utilsFile);
                project.AddFile(sciFile);
                solution.AddProject(project);

                // 3. Prepare analyzer registry and embedding infrastructure.
                var registry = new AnalyzerRegistry();
                registry.RegisterAnalyzer(new RoslynCodeAnalyzer());

                var vectorStore = new InMemoryVectorStore();
                var storeActor = new EmbeddingStoreActor("vector-store", vectorStore);
                IEmbeddingGenerator embeddingGen;
                try
                {
                    var http = new System.Net.Http.HttpClient { BaseAddress = new Uri("http://localhost:11434") };
                    embeddingGen = new OllamaEmbeddingGenerator(http);
                }
                catch (Exception)
                {
                    // Fallback for environments without Ollama running.
                    embeddingGen = new StubEmbeddingGenerator();
                }

                // 4. Spawn application agents.
                const string indexerId = "indexer-agent";
                const string searchId  = "search-agent";
                const string llmId     = "llm-agent";
                const string intentId  = "intent-agent";
                const string queryId   = "query-agent";
                const string coderId   = "coder-agent";
                const string refactorId = "refactor-agent";

                var indexerAgent = new IndexerAgent(indexerId, registry, embeddingGen, storeActor);
                indexerAgent.Configure(registry, embeddingGen, storeActor, solution);

                var resolvers = new List<IIntentResolver>
                {
                    new RegexIntentResolver(),
                    new HeuristicIntentResolver(),
                    new ProxyIntentResolver(_runtimeAdapter, intentId)
                };
                var searchAgent  = new SearchAgent(searchId, embeddingGen, storeActor, solution, resolvers);

                var llmAgent     = new LLMAgent(llmId); // Uses default Ollama settings – OK for demo.

                // IntentDetectionAgent (LLM-based)
                var httpCli = new System.Net.Http.HttpClient { BaseAddress = new Uri("http://localhost:11434") };
                ILlmClient llmClient = new OllamaLlmClient(httpCli);
                var intentAgent = new IntentDetectionAgent(intentId, llmClient);

                // Manually initialize and then register the prebuilt agents
                foreach (var agent in new Agent[] { indexerAgent, searchAgent, llmAgent, intentAgent })
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

                // Ensure factory knows core tool and coder agents
                agentFactory.RegisterAgentType<AgctorSDK.Core.Tools.Implementations.CodeEditorTool>();
                agentFactory.RegisterAgentType<AgctorSDK.Core.Tools.Implementations.CompileTool>();
                agentFactory.RegisterAgentType<AgctorSDK.Core.Tools.Implementations.TestRunnerTool>();
                agentFactory.RegisterAgentType<AgctorSDK.Core.Agents.CoderAgent>();
                agentFactory.RegisterAgentType<AgctorSDK.CodeGraph.Agents.RefactorAgent>();

                spawnedQuery.SetAgentFactory(agentFactory);
                await _agentRegistry.RegisterAgentAsync(spawnedQuery);

                // Spawn CoderAgent for editing/building code
                var spawnedCoder = await _runtimeAdapter.SpawnActorAsync<CoderAgent>(
                    coderId,
                    id => new CoderAgent(id));

                spawnedCoder.SetAgentFactory(agentFactory);
                await _agentRegistry.RegisterAgentAsync(spawnedCoder);

                // Spawn RefactorAgent
                var spawnedRefactor = await _runtimeAdapter.SpawnActorAsync<RefactorAgent>(
                    refactorId,
                    id => new RefactorAgent(id, searchId, llmId, coderId));

                spawnedRefactor.SetAgentFactory(agentFactory);
                await _agentRegistry.RegisterAgentAsync(spawnedRefactor);

                _logger.LogInformation("CodeGraph demo scenario set up – agents ready (Indexer, Search, LLM, Query, Coder, Refactor)");

                // Register CodeGraph context for dashboard (PRD-006): actor tree + embedding store summary
                var actorTree = ActorSerializer.ToDto(solution);
                var vectorCount = await vectorStore.CountAsync();
                _codeGraphContextAccessor.SetCurrent(new CodeGraphContextDto
                {
                    ActorTree = actorTree,
                    EmbeddingStoreSummary = new EmbeddingStoreSummaryDto { VectorCount = vectorCount }
                });
                // Live actor tree so dashboard shows Class/Method after Index and reflects code changes
                _codeGraphContextAccessor.SetActorTreeProvider(() => ActorSerializer.ToDto(solution));
                // Live embedding count so dashboard "Index now" updates the displayed count
                _codeGraphContextAccessor.SetEmbeddingCountProvider(ct => vectorStore.CountAsync());
                // Embedding records for debugging/visualization (GET /api/CodeGraph/embeddings)
                _codeGraphContextAccessor.SetEmbeddingRecordsProvider(async ct =>
                {
                    var records = await vectorStore.GetAllAsync();
                    return records.Select(r => new EmbeddingRecordDto
                    {
                        ActorId = r.ActorId,
                        Text = r.Text,
                        VectorLength = r.Vector.Length,
                        Vector = r.Vector
                    }).ToList();
                });

                var created = new List<string> { indexerId, searchId, llmId, queryId, coderId, refactorId };
                var roles = new Dictionary<string, string>
                {
                    [indexerId] = "Indexes CodeGraph and stores embeddings",
                    [searchId]  = "Vector search over CodeGraph",
                    [llmId]     = "Large-language-model interface (Ollama)",
                    [queryId]   = "Query orchestrator",
                    [coderId]   = "Code editing / compile / test orchestration",
                    [refactorId] = "End-to-end refactor orchestrator"
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
        private const string CalculatorSource = @"namespace DemoApp
{
    public class Calculator
    {
        public int Add(int a, int b) => a + b;
        public int Subtract(int a, int b) => a - b;
        public int Multiply(int a, int b) => a * b;

        public double Divide(int a, int b)
        {
            if (b == 0) throw new System.DivideByZeroException();
            return (double)a / b;
        }

        // Sum of an arbitrary list of integers
        public int Sum(params int[] numbers) => System.Linq.Enumerable.Sum(numbers);
    }
}";

        private const string MathUtilsSource = @"namespace DemoApp
{
    public static class MathUtils
    {
        public static int Square(int x) => x * x;
        public static int Cube(int x)   => x * x * x;
    }
}";

        private const string ScientificCalculatorSource = @"namespace DemoApp
{
    public class ScientificCalculator : Calculator
    {
        public double Power(double @base, double exp) => System.Math.Pow(@base, exp);
        public double Sqrt(double x) => System.Math.Sqrt(x);
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