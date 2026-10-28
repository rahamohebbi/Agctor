using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Registry;
using AgctorSDK.Core.Sessions;
using AgctorSDK.Core.Streaming;
using AgctorSDK.Extensions.DependencyInjection;
using AgctorSDK.Core.ProjectMemory.Scenarios;
using AgctorSDK.Host.Mcp;
using AgctorSDK.Host.Models;
using AgctorSDK.Host.Services;
using AgctorSDK.Host.Services.ProjectMemory;
using AgctorSDK.Host.Services.Scenarios;
using AgctorSDK.Host.Services.Sessions;
using AgctorSDK.Host.Services.Traces;
using AgctorSDK.Host.Services.Visual;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgctorSDK.Host.DependencyInjection;

/// <summary>HTTP/MCP/dashboard-specific services that stay in the Host assembly.</summary>
public static class HostWebServiceExtensions
{
    public static IServiceCollection AddAgctorHostWeb(
        this IServiceCollection services,
        IConfiguration configuration,
        int configuredMcpPort)
    {
        services.AddSingleton<AgctorSDK.Core.ProjectMemory.Resolution.Trace.IResolveSpanSink, ResolveSpanTraceSink>();
        services.AddSingleton<IProjectMemoryFileService, ProjectMemoryFileService>();
        services.AddSingleton<IProjectMemoryAgentYamlPersistence, ProjectMemoryAgentYamlPersistence>();
        services.AddSingleton<IUserProjectMemorySettingsService, UserProjectMemorySettingsService>();
        services.AddSingleton<IPlaygroundChatSettingsService, PlaygroundChatSettingsService>();
        services.AddSingleton<ILlmUserSettingsService, LlmUserSettingsService>();
        services.AddSingleton<IOllamaModelCatalog, OllamaModelCatalog>();
        services.AddSingleton<AgctorSDK.Host.Services.Models.ModelSelectionService>();
        services.AddSingleton<AgctorSDK.Host.Services.Models.ModelCapabilitiesService>();
        services.AddHttpClient(OllamaDecisionClient.HttpClientName, client => client.Timeout = TimeSpan.FromMinutes(2));
        services.AddSingleton<IOllamaDecisionClient, OllamaDecisionClient>();
        services.AddSingleton<ScenarioFlowDecisionRouter>();

        services.AddSingleton<IAgentRegistry, InMemoryAgentRegistry>();
        services.AddSingleton<IAgentOutputStreamRegistry, AgentOutputStreamRegistry>();
        services.AddSingleton<IMessageDispatcher, MessageDispatcher>();
        services.AddAgctorToolCatalog(configuration);
        services.AddSingleton<IToolAgentsInsightService, ToolAgentsInsightService>();
        services.AddSingleton<IPersonaHostToolsService, PersonaHostToolsService>();
        services.AddSingleton<IToolInvoker, ToolInvoker>();

        services.AddAgctorSceneMemory(configuration["Agctor:SceneMemory:StorePath"]
            ?? Path.Combine(AppContext.BaseDirectory, "data", "scene-memory.db"));

        var sessionStorePath = configuration.GetValue<string>("Agctor:SessionStorePath")
            ?? Path.Combine(AppContext.BaseDirectory, "data", $"agctor-sessions-{configuredMcpPort}.db");
        services.AddSingleton(new SessionMemoryOptions
        {
            RecentTurnWindow = configuration.GetValue<int?>("Agctor:SessionMemory:RecentTurnWindow") ?? 8,
            SummaryRefreshTurns = configuration.GetValue<int?>("Agctor:SessionMemory:SummaryRefreshTurns") ?? 12,
            MaxContextChars = configuration.GetValue<int?>("Agctor:SessionMemory:MaxContextChars") ?? 12000
        });
        services.AddSingleton<SqliteSessionStore>(_ => new SqliteSessionStore(sessionStorePath));
        services.AddSingleton<ISessionStore>(sp => sp.GetRequiredService<SqliteSessionStore>());
        services.AddSingleton<AgctorSDK.Core.ProjectMemory.Rag.IRagInspectionStore>(sp => sp.GetRequiredService<SqliteSessionStore>());
        // Replace the generic optional-dependency registration with an explicit host instance.
        // This guarantees that Playground retrieval and its inspector write to the same durable session store.
        services.AddSingleton<AgctorSDK.Core.ProjectMemory.Rag.RagContextService>(sp => new(
            sp.GetRequiredService<AgctorSDK.Core.Rag.IRagProviderAdapterFactory>(),
            sp.GetRequiredService<AgctorSDK.Core.ProjectMemory.Rag.IRagInspectionStore>(),
            sp.GetService<AgctorSDK.Core.Utils.ActivityTracking.IActivityTracker>()));
        var traceStorePath = configuration.GetValue<string>("Agctor:TraceStorePath")
            ?? Path.Combine(AppContext.BaseDirectory, "data", $"agctor-traces-{configuredMcpPort}.db");
        services.AddSingleton<ITraceTimelineStore>(_ => new SqliteTraceTimelineStore(traceStorePath));
        services.AddSingleton<ISessionContextComposer, SessionContextComposer>();
        services.AddSingleton<Services.Rag.RagMigrationService>(sp => new(
            sp.GetRequiredService<AgctorSDK.Core.Rag.IRagProviderAdapterFactory>(),
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<AgctorSDK.Core.ProjectMemory.ProjectMemoryAgentOptions>>(),
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<AgctorSDK.Core.Rag.RagOptions>>(),
            sp.GetRequiredService<IScenarioCatalog>(), sessionStorePath + ".rag-migrations"));
        services.AddHostedService(sp => sp.GetRequiredService<Services.Rag.RagMigrationService>());


        services.Configure<ScenarioCatalogOptions>(configuration.GetSection("Agctor:Scenarios"));
        services.AddSingleton<IScenarioCatalog, JsonScenarioCatalog>();
        services.AddSingleton<IScenarioFactory, ScenarioFactory>();
        services.AddSingleton<ICurrentScenarioStore, CurrentScenarioStore>();
        services.AddSingleton<IScenarioApplicationService, ScenarioApplicationService>();
        services.AddSingleton<IProjectMemoryPersonaLlmRunner, ProjectMemoryPersonaLlmRunner>();
        services.AddSingleton<IScenarioFlowPersonaLlmRunner, ScenarioFlowPersonaLlmRunner>();
        services.AddSingleton<PlaygroundStyleRefreshService>();
        services.AddSingleton<IScenarioFlowRouterLlmService, ScenarioFlowRouterLlmService>();
        services.AddSingleton<IScenarioFlowRuntimeStore, ScenarioFlowRuntimeStore>();
        services.AddSingleton<IScenarioFlowSegmentExecutor, ScenarioFlowSegmentExecutor>();
        services.Configure<ScenarioFlowHostOptions>(configuration.GetSection("Agctor:ScenarioFlow"));
        services.AddSingleton<IScenarioFlowRuntimeOrchestrator, ScenarioFlowRuntimeOrchestrator>();
        services.AddSingleton<IScenarioFlowDomainEventPublisher, ScenarioFlowDomainEventPublisher>();
        services.AddSingleton<PlaygroundScenarioFlowV2Runner>();
        services.AddSingleton<IScenarioFlowExecutionService, ScenarioFlowExecutionService>();
        services.AddSingleton<IAgentTypeEnablementService, AgentTypeEnablementService>();
        services.AddSingleton<VisualTranscriptEnricher>();
        services.AddSingleton<VisualPlaygroundAttachmentService>();
        services.AddSingleton<VisualPlaygroundStreamExtractService>();
        services.AddSingleton<GenericInboxVisualEnricher>();
        services.AddSingleton<VisualIngestToolBridge>();
        services.AddSingleton<VisualExtractToolBridge>();
        services.AddSingleton<PlaygroundFocusPostHook>();
        services.AddSingleton<IHostConfigurationService, HostConfigurationService>();
        services.AddSingleton<IUserRuntimeSettingsService, UserRuntimeSettingsService>();
        services.AddSingleton<IActorRuntimeDockerService, ActorRuntimeDockerService>();
        services.AddSingleton<IRagProviderDockerService, RagProviderDockerService>();
        services.AddSingleton<IUserRagSettingsService, UserRagSettingsService>();
        services.AddSingleton<ITerminalCommandService, TerminalCommandService>();
        services.AddSingleton<IActorRuntimeSwitchService, ActorRuntimeSwitchService>();
        services.AddSingleton<IRuntimeDashboardService, RuntimeDashboardService>();
        services.AddSingleton<IRagProvidersDashboardService, RagProvidersDashboardService>();
        services.AddSingleton<AgctorSDK.Core.Interfaces.IAgentDetailProvider, Services.AgentDetailProviders.LLMAgentDetailProvider>();
        services.AddSingleton<AgctorSDK.Core.Interfaces.IAgentDetailProvider, Services.AgentDetailProviders.CoderAgentDetailProvider>();
        services.AddSingleton<AgctorSDK.Core.Interfaces.IAgentDetailProviderRegistry, AgentDetailProviderRegistry>();
        services.AddSingleton<ICodeGraphContextAccessor, CodeGraphContextAccessor>();
        services.AddSingleton<McpListener>();
        services.AddSingleton<McpEndpointInfo>();

        return services;
    }
}
