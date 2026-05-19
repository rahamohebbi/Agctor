using AgctorSDK.Agents;
using AgctorSDK.Agents.ProjectMemory;
using AgctorSDK.CodeGraph.Llm;
using AgctorSDK.Core.Adapters;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.DependencyInjection;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Ollama;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Orchestration;
using AgctorSDK.Core.ProjectMemory.OutOfSchema;
using AgctorSDK.Core.ProjectMemory.Resolution.Trace;
using AgctorSDK.Core.Runtime;
using AgctorSDK.Extensions.DependencyInjection;
using AgctorSDK.Extensions.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgctorSDK.Extensions.Hosting;

/// <summary>Registers portable host/CLI SDK services (project memory, runtime, tasks, LLM) — not HTTP/MCP/dashboard types.</summary>
public static class HostServiceCollectionExtensions
{
    public static IServiceCollection AddAgctorHost(
        this IServiceCollection services,
        IConfiguration configuration,
        string? defaultProjectMemoryRoot = null)
    {
        services.ConfigureAgentTypes();
        services.ConfigureProjectMemoryOptions(configuration, defaultProjectMemoryRoot);
        services.AddAgctorProjectMemory();
        services.AddAgctorCompanionMemory();
        services.AddAgctorResolution();

        services.TryAddSingleton<IProjectMemoryLlmClient, OllamaConfiguredCompletionClient>();
        services.TryAddSingleton<IConfirmationIntentClassifier, LlmConfirmationIntentClassifier>();
        services.AddSingleton<ProjectMemoryPipelineRunner>();
        services.AddSingleton<IProjectMemoryPipelineRunner>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ProjectMemoryAgentOptions>>().Value;
            return options.ExecutionMode == ProjectMemoryPipelineExecutionMode.ActorWorkflow
                ? new ActorBackedProjectMemoryPipelineRunner(
                    sp.GetRequiredService<IActorRuntimeAdapter>(),
                    sp.GetRequiredService<ProjectMemoryPipelineRunner>())
                : sp.GetRequiredService<ProjectMemoryPipelineRunner>();
        });

        services.AddAgctorRuntimeFromConfiguration(configuration);
        services.AddAgctorActivityTracking(opts => opts.EnableToolTracing = true);
        services.AddAgctorVisualization();
        services.AddInMemoryTaskStore();
        services.AddInMemoryGoalStore();
        services.AddPullRequestAutomation();
        services.AddCodeGraphGeneration();
        services.ConfigureHostBackgroundWorkers(configuration);

        services.AddHttpClient<OllamaLlmClient>();
        services.AddSingleton<ILlmClient>(sp => sp.GetRequiredService<OllamaLlmClient>());

        return services;
    }

    /// <summary>Optional hook for host assembly to register <see cref="IResolveSpanSink"/> and other host-only types.</summary>
    public static IServiceCollection AddAgctorHostResolveSpanSink<TSink>(this IServiceCollection services)
        where TSink : class, IResolveSpanSink
    {
        services.TryAddSingleton<IResolveSpanSink, TSink>();
        return services;
    }

    private static void ConfigureAgentTypes(this IServiceCollection services)
    {
        services.Configure<AgentTypeOptions>(options =>
        {
            options.RegisterAgentType("Agent", typeof(Agent));
            options.RegisterAgentType("LLMAgent", typeof(LLMAgent));
            options.RegisterAgentType("CoderAgent", typeof(CoderAgent));
            options.RegisterAgentType("SessionCoordinatorAgent", typeof(SessionCoordinatorAgent));
            options.RegisterAgentType("SessionMemoryAgent", typeof(SessionMemoryAgent));
            options.RegisterAgentType("PersonExtractorProjectAgent", typeof(PersonExtractorProjectAgent));
            options.RegisterAgentType("MemoryCuratorProjectAgent", typeof(MemoryCuratorProjectAgent));
            options.RegisterAgentType("PersonQueryProjectAgent", typeof(PersonQueryProjectAgent));
        });
    }

    private static void ConfigureProjectMemoryOptions(
        this IServiceCollection services,
        IConfiguration configuration,
        string? defaultProjectMemoryRoot)
    {
        services.Configure<ProjectMemoryAgentOptions>(o =>
        {
            var cfgPath = configuration["Agctor:ProjectMemory:ProjectRoot"];
            o.ProjectRoot = !string.IsNullOrWhiteSpace(cfgPath)
                ? Path.GetFullPath(cfgPath)
                : defaultProjectMemoryRoot ?? string.Empty;
            if (Enum.TryParse<ProjectMemoryPipelineExecutionMode>(
                    configuration["Agctor:ProjectMemory:ExecutionMode"],
                    ignoreCase: true,
                    out var executionMode))
            {
                o.ExecutionMode = executionMode;
            }
        });
    }

    private static void AddAgctorRuntimeFromConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        var configured = AgctorRuntimeCatalog.NormalizeRuntimeName(
            configuration.GetValue<string>("Agctor:DefaultRuntime"));
        var allowExperimental = configuration.GetValue("Agctor:AllowExperimentalRuntimes", false);

        if (AgctorRuntimeCatalog.IsExperimental(configured) && !allowExperimental)
        {
            Console.WriteLine(
                $"⚠️ Runtime '{configured}' is experimental; using InMemory. Set Agctor:AllowExperimentalRuntimes=true to enable.");
            configured = AgctorRuntimeCatalog.InMemory;
        }

        Console.WriteLine($"🔄 Configured actor runtime: {configured}");

        switch (configured)
        {
            case AgctorRuntimeCatalog.ProtoActor:
                services.AddAgctor<ProtoActorAdapter>(opts =>
                {
                    opts.DefaultRuntime = AgctorRuntimeCatalog.ProtoActor;
                    opts.AllowExperimentalRuntimes = allowExperimental;
                });
                break;
            case AgctorRuntimeCatalog.Orleans:
                services.AddAgctor<OrleansAdapter>(opts =>
                {
                    opts.DefaultRuntime = AgctorRuntimeCatalog.Orleans;
                    opts.AllowExperimentalRuntimes = allowExperimental;
                });
                break;
            default:
                services.AddAgctor<InMemoryActorRuntime>(opts =>
                {
                    opts.DefaultRuntime = AgctorRuntimeCatalog.InMemory;
                    opts.AllowExperimentalRuntimes = allowExperimental;
                });
                break;
        }
    }

    private static void ConfigureHostBackgroundWorkers(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TaskScoperHostedService.TaskScoperOptions>(options =>
        {
            var seconds = configuration.GetValue<int?>("TaskScoper:ScanInterval");
            if (seconds is > 0)
                options.ScanInterval = TimeSpan.FromSeconds(seconds.Value);
        });
        services.Configure<TaskFlowHostedService.TaskFlowOptions>(options =>
        {
            var seconds = configuration.GetValue<int?>("TaskFlow:Interval");
            if (seconds is > 0)
                options.Interval = TimeSpan.FromSeconds(seconds.Value);
        });
        services.AddSingleton<TaskScoperHostedService>();
        services.AddSingleton<TaskFlowHostedService>();
    }
}
