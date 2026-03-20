using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
using AgctorSDK.CodeGraph.Messages;
using AgctorSDK.Core.Sessions;

namespace AgctorSDK.Host.Services.Scenarios
{
    /// <summary>
    /// Demonstrates the CodeGraph subsystem in action by creating a minimal hierarchy (Solution → Project → File)
    /// and an <see cref="IndexerAgent"/> that walks the graph and stores embeddings in an in-memory vector store.
    /// </summary>
    public sealed class CodeGraphDemoScenario : IScenario
    {
        private static readonly string[] DemoAgentIds =
        {
            "indexer-agent",
            "embedding-coordinator-agent",
            "search-agent",
            "llm-agent",
            "intent-agent",
            "query-agent",
            "coder-agent",
            "refactor-agent",
            "session-coordinator-agent"
        };

        private readonly IActorRuntimeAdapter _runtimeAdapter;
        private readonly IAgentRegistry _agentRegistry;
        private readonly ICodeGraphContextAccessor _codeGraphContextAccessor;
        private readonly ISessionStore _sessionStore;
        private readonly ISessionContextComposer _sessionContextComposer;
        private readonly SessionMemoryOptions _sessionMemoryOptions;
        private readonly ILogger<CodeGraphDemoScenario> _logger;

        public string Name => "code-graph-demo";
        public string Description => "Creates a minimal CodeGraph with an IndexerAgent that indexes a sample C# file";

        public CodeGraphDemoScenario(
            IActorRuntimeAdapter runtimeAdapter,
            IAgentRegistry agentRegistry,
            ICodeGraphContextAccessor codeGraphContextAccessor,
            ISessionStore sessionStore,
            ISessionContextComposer sessionContextComposer,
            SessionMemoryOptions sessionMemoryOptions,
            ILogger<CodeGraphDemoScenario> logger)
        {
            _runtimeAdapter = runtimeAdapter;
            _agentRegistry = agentRegistry;
            _codeGraphContextAccessor = codeGraphContextAccessor ?? throw new ArgumentNullException(nameof(codeGraphContextAccessor));
            _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
            _sessionContextComposer = sessionContextComposer ?? throw new ArgumentNullException(nameof(sessionContextComposer));
            _sessionMemoryOptions = sessionMemoryOptions ?? throw new ArgumentNullException(nameof(sessionMemoryOptions));
            _logger = logger;
        }

        public async Task<ScenarioSetupResponse> SetupAsync(Dictionary<string, object>? parameters = null)
        {
            try
            {
                var useStubEmbeddings = parameters != null &&
                    parameters.TryGetValue("useStubEmbeddings", out var useStubEmbeddingsValue) &&
                    bool.TryParse(useStubEmbeddingsValue?.ToString(), out var parsedUseStubEmbeddings) &&
                    parsedUseStubEmbeddings;

                await ResetExistingDemoAgentsAsync();

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
                IEmbeddingGenerator embeddingGen = new StubEmbeddingGenerator();
                if (!useStubEmbeddings)
                {
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
                }

                // 4. Spawn application agents.
                const string indexerId = "indexer-agent";
                const string searchId  = "search-agent";
                const string llmId     = "llm-agent";
                const string intentId  = "intent-agent";
                const string queryId   = "query-agent";
                const string coderId   = "coder-agent";
                const string refactorId = "refactor-agent";
                const string embeddingCoordinatorId = "embedding-coordinator-agent";
                const string sessionCoordinatorId = "session-coordinator-agent";

                // Inject an AgentFactory so agents can use the runtime for shared orchestration.
                var consoleLogger = new AgctorConsoleLogger();
                var sp = new Microsoft.Extensions.DependencyInjection.ServiceCollection().BuildServiceProvider();
                var agentFactory = new AgctorSDK.Core.Agents.AgentFactory(_runtimeAdapter, sp, consoleLogger, _agentRegistry);
                agentFactory.RegisterAgentType<AgctorSDK.Core.Tools.Implementations.CodeEditorTool>();
                agentFactory.RegisterAgentType<AgctorSDK.Core.Tools.Implementations.CompileTool>();
                agentFactory.RegisterAgentType<AgctorSDK.Core.Tools.Implementations.TestRunnerTool>();
                agentFactory.RegisterAgentType<AgctorSDK.Core.Agents.CoderAgent>();
                agentFactory.RegisterAgentType<AgctorSDK.CodeGraph.Agents.RefactorAgent>();
                agentFactory.RegisterAgentType<AgctorSDK.Core.Agents.SessionCoordinatorAgent>();

                var indexerAgent = new IndexerAgent(indexerId, registry, embeddingGen, storeActor);
                indexerAgent.Configure(registry, embeddingGen, storeActor, solution);
                indexerAgent.SetAgentFactory(agentFactory);

                var embeddingCoordinatorAgent = new EmbeddingCoordinatorAgent(embeddingCoordinatorId, indexerId);
                embeddingCoordinatorAgent.Configure(indexerId);
                embeddingCoordinatorAgent.SetAgentFactory(agentFactory);

                var resolvers = new List<IIntentResolver>
                {
                    new RegexIntentResolver(),
                    new HeuristicIntentResolver(),
                    new ProxyIntentResolver(_runtimeAdapter, intentId)
                };
                var searchAgent  = new SearchAgent(searchId, embeddingGen, storeActor, solution, resolvers, embeddingCoordinatorId);
                searchAgent.SetAgentFactory(agentFactory);

                var llmAgent     = new LLMAgent(llmId); // Uses default Ollama settings – OK for demo.
                llmAgent.SetAgentFactory(agentFactory);

                // IntentDetectionAgent (LLM-based)
                var httpCli = new System.Net.Http.HttpClient { BaseAddress = new Uri("http://localhost:11434") };
                ILlmClient llmClient = new OllamaLlmClient(httpCli);
                var intentAgent = new IntentDetectionAgent(intentId, llmClient);
                intentAgent.SetAgentFactory(agentFactory);

                // Manually initialize and then register the prebuilt agents
                foreach (var agent in new Agent[] { indexerAgent, embeddingCoordinatorAgent, searchAgent, llmAgent, intentAgent })
                {
                    await agent.InitializeAsync();
                    await _runtimeAdapter.RegisterActorAsync(agent);
                    await _agentRegistry.RegisterAgentAsync(agent);
                }

                // Spawn QueryAgent via runtime so AgentFactory gets injected automatically
                var spawnedQuery = await _runtimeAdapter.SpawnActorAsync<QueryAgent>(
                    queryId,
                    id => new QueryAgent(id, searchId, llmId));

                spawnedQuery.SetAgentFactory(agentFactory);
                await _agentRegistry.RegisterAgentAsync(spawnedQuery);

                // Spawn CoderAgent for editing/building code
                var spawnedCoder = await _runtimeAdapter.SpawnActorAsync<CoderAgent>(
                    coderId,
                    id => new CoderAgent(id));

                spawnedCoder.SetAgentFactory(agentFactory);
                spawnedCoder.ConfigureEmbeddingCoordinator(embeddingCoordinatorId);
                await _agentRegistry.RegisterAgentAsync(spawnedCoder);

                // Spawn RefactorAgent
                var spawnedRefactor = await _runtimeAdapter.SpawnActorAsync<RefactorAgent>(
                    refactorId,
                    id => new RefactorAgent(id, searchId, llmId, coderId));

                spawnedRefactor.SetAgentFactory(agentFactory);
                await _agentRegistry.RegisterAgentAsync(spawnedRefactor);

                var existingSessionCoordinator = await _agentRegistry.GetAgentByIdAsync(sessionCoordinatorId);
                if (existingSessionCoordinator is SessionCoordinatorAgent registeredSessionCoordinator)
                {
                    registeredSessionCoordinator.SetAgentFactory(agentFactory);
                }
                else
                {
                    var sessionCoordinator = await _runtimeAdapter.SpawnActorAsync<SessionCoordinatorAgent>(
                        sessionCoordinatorId,
                        id => new SessionCoordinatorAgent(id, _sessionStore, _sessionContextComposer, _sessionMemoryOptions));
                    sessionCoordinator.SetAgentFactory(agentFactory);
                    await _agentRegistry.RegisterAgentAsync(sessionCoordinator);
                }

                _logger.LogInformation("CodeGraph demo scenario set up – agents ready (Indexer, Search, LLM, Query, Coder, Refactor)");

                // Register CodeGraph context for dashboard (PRD-006): actor tree + embedding store summary
                var actorTree = ActorSerializer.ToDto(solution);
                var vectorCount = await vectorStore.CountAsync();
                _codeGraphContextAccessor.SetCurrent(new CodeGraphContextDto
                {
                    ActorTree = actorTree,
                    EmbeddingStoreSummary = new EmbeddingStoreSummaryDto
                    {
                        VectorCount = vectorCount,
                        State = EmbeddingLifecycleState.NotReady.ToString(),
                        IsReady = false,
                        GraphVersion = 1,
                        IndexedGraphVersion = 0
                    }
                });
                // Live actor tree so dashboard shows Class/Method after Index and reflects code changes
                _codeGraphContextAccessor.SetActorTreeProvider(() => ActorSerializer.ToDto(solution));
                // Live embedding count so dashboard "Index now" updates the displayed count
                _codeGraphContextAccessor.SetEmbeddingCountProvider(ct => vectorStore.CountAsync());
                _codeGraphContextAccessor.SetEmbeddingSummaryProvider(async ct =>
                {
                    var status = await _runtimeAdapter.SendMessageAsync<EmbeddingStatusResult>(
                        embeddingCoordinatorId,
                        new GetEmbeddingStatusMessage(),
                        timeout: TimeSpan.FromSeconds(15),
                        senderId: Name,
                        headers: new Dictionary<string, string> { ["MessageType"] = "GetEmbeddingStatus" },
                        cancellationToken: ct);

                    var count = await vectorStore.CountAsync();
                    return new EmbeddingStoreSummaryDto
                    {
                        VectorCount = count,
                        State = status.State.ToString(),
                        IsReady = status.IsReady,
                        GraphVersion = status.GraphVersion,
                        IndexedGraphVersion = status.IndexedGraphVersion,
                        LastIndexedAt = status.LastIndexedAt,
                        LastError = status.LastError
                    };
                });
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

                var activeDemoAgentIds = (await _agentRegistry.GetAllAgentIdsAsync())
                    .Where(id => DemoAgentIds.Contains(id, StringComparer.Ordinal))
                    .OrderBy(id => Array.IndexOf(DemoAgentIds, id))
                    .ToList();

                var missingAgentIds = DemoAgentIds
                    .Except(activeDemoAgentIds, StringComparer.Ordinal)
                    .ToList();

                _logger.LogInformation(
                    "CodeGraph demo active agents after setup: {AgentIds}",
                    string.Join(", ", activeDemoAgentIds));

                if (missingAgentIds.Count > 0)
                {
                    var error = $"CodeGraph demo setup incomplete. Missing agents: {string.Join(", ", missingAgentIds)}";
                    _logger.LogError(error);
                    return new ScenarioSetupResponse(false, Name, activeDemoAgentIds, new Dictionary<string, string>(), error);
                }

                var roles = new Dictionary<string, string>
                {
                    [indexerId] = "Indexes CodeGraph and stores embeddings",
                    [embeddingCoordinatorId] = "Coordinates embedding lifecycle and freshness",
                    [searchId]  = "Vector search over CodeGraph",
                    [llmId]     = "Large-language-model interface (Ollama)",
                    [intentId]  = "Intent detection for code search prompts",
                    [queryId]   = "Query orchestrator",
                    [coderId]   = "Code editing / compile / test orchestration",
                    [refactorId] = "End-to-end refactor orchestrator",
                    [sessionCoordinatorId] = "Session memory coordinator for chat continuity"
                };

                return new ScenarioSetupResponse(true, Name, activeDemoAgentIds, roles, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set up CodeGraph demo scenario");
                return new ScenarioSetupResponse(false, Name, new List<string>(), new Dictionary<string, string>(), ex.Message);
            }
        }

        private async Task ResetExistingDemoAgentsAsync()
        {
            _logger.LogInformation("Resetting existing CodeGraph demo agents before setup");

            _codeGraphContextAccessor.SetCurrent(null);
            _codeGraphContextAccessor.SetActorTreeProvider(null);
            _codeGraphContextAccessor.SetEmbeddingCountProvider(null);
            _codeGraphContextAccessor.SetEmbeddingSummaryProvider(null);
            _codeGraphContextAccessor.SetEmbeddingRecordsProvider(null);

            foreach (var agentId in DemoAgentIds)
            {
                try
                {
                    await _runtimeAdapter.StopActorAsync(agentId);
                    _logger.LogInformation("Stopped existing demo agent {AgentId}", agentId);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Ignoring stop failure for demo agent {AgentId}", agentId);
                }

                try
                {
                    await _agentRegistry.UnregisterAgentAsync(agentId);
                    _logger.LogInformation("Unregistered existing demo agent {AgentId}", agentId);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Ignoring registry cleanup failure for demo agent {AgentId}", agentId);
                }
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