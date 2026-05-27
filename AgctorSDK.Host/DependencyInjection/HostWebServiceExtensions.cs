using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Registry;
using AgctorSDK.Core.Sessions;
using AgctorSDK.Core.Streaming;
using AgctorSDK.Extensions.DependencyInjection;
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
        services.AddSingleton<ILlmUserSettingsService, LlmUserSettingsService>();
        services.AddSingleton<IOllamaModelCatalog, OllamaModelCatalog>();

        services.AddSingleton<IAgentRegistry, InMemoryAgentRegistry>();
        services.AddSingleton<IAgentOutputStreamRegistry, AgentOutputStreamRegistry>();
        services.AddSingleton<IMessageDispatcher, MessageDispatcher>();
        services.AddAgctorToolCatalog(configuration);
        services.AddSingleton<IToolAgentsInsightService, ToolAgentsInsightService>();
        services.AddSingleton<IToolInvoker, ToolInvoker>();

        var sessionStorePath = configuration.GetValue<string>("Agctor:SessionStorePath")
            ?? Path.Combine(AppContext.BaseDirectory, "data", $"agctor-sessions-{configuredMcpPort}.db");
        services.AddSingleton(new SessionMemoryOptions
        {
            RecentTurnWindow = configuration.GetValue<int?>("Agctor:SessionMemory:RecentTurnWindow") ?? 8,
            SummaryRefreshTurns = configuration.GetValue<int?>("Agctor:SessionMemory:SummaryRefreshTurns") ?? 12,
            MaxContextChars = configuration.GetValue<int?>("Agctor:SessionMemory:MaxContextChars") ?? 12000
        });
        services.AddSingleton<ISessionStore>(_ => new SqliteSessionStore(sessionStorePath));
        var traceStorePath = configuration.GetValue<string>("Agctor:TraceStorePath")
            ?? Path.Combine(AppContext.BaseDirectory, "data", $"agctor-traces-{configuredMcpPort}.db");
        services.AddSingleton<ITraceTimelineStore>(_ => new SqliteTraceTimelineStore(traceStorePath));
        services.AddSingleton<ISessionContextComposer, SessionContextComposer>();

        services.Configure<ScenarioCatalogOptions>(configuration.GetSection("Agctor:Scenarios"));
        services.AddSingleton<IScenarioCatalog, JsonScenarioCatalog>();
        services.AddSingleton<IScenarioFactory, ScenarioFactory>();
        services.AddSingleton<ICurrentScenarioStore, CurrentScenarioStore>();
        services.AddSingleton<IScenarioApplicationService, ScenarioApplicationService>();
        services.AddSingleton<IProjectMemoryPersonaLlmRunner, ProjectMemoryPersonaLlmRunner>();
        services.AddSingleton<IScenarioFlowRouterLlmService, ScenarioFlowRouterLlmService>();
        services.AddSingleton<IScenarioFlowExecutionService, ScenarioFlowExecutionService>();
        services.AddSingleton<IAgentTypeEnablementService, AgentTypeEnablementService>();
        services.AddSingleton<VisualTranscriptEnricher>();
        services.AddSingleton<VisualPlaygroundAttachmentService>();
        services.AddSingleton<GenericInboxVisualEnricher>();
        services.AddSingleton<VisualIngestToolBridge>();
        services.AddSingleton<PlaygroundFocusPostHook>();
        services.AddSingleton<IHostConfigurationService, HostConfigurationService>();
        services.AddSingleton<IUserRuntimeSettingsService, UserRuntimeSettingsService>();
        services.AddSingleton<AgctorSDK.Core.Interfaces.IAgentDetailProvider, Services.AgentDetailProviders.LLMAgentDetailProvider>();
        services.AddSingleton<AgctorSDK.Core.Interfaces.IAgentDetailProvider, Services.AgentDetailProviders.CoderAgentDetailProvider>();
        services.AddSingleton<AgctorSDK.Core.Interfaces.IAgentDetailProviderRegistry, AgentDetailProviderRegistry>();
        services.AddSingleton<ICodeGraphContextAccessor, CodeGraphContextAccessor>();
        services.AddSingleton<McpListener>();
        services.AddSingleton<McpEndpointInfo>();

        return services;
    }
}
