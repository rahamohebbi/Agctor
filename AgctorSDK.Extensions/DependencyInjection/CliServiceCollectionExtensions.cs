using AgctorSDK.Core.DependencyInjection;
using AgctorSDK.Core.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgctorSDK.Extensions.DependencyInjection;

/// <summary>CLI entry-point DI: same SDK stack as Host (Core, Agents, Tools, CodeGraph) without ASP.NET services.</summary>
public static class CliServiceCollectionExtensions
{
    public static IServiceCollection AddAgctorCli(
        this IServiceCollection services,
        IConfiguration? configuration = null,
        string? runtimeName = null,
        Action<AgctorOptions>? configureOptions = null)
    {
        configuration ??= new ConfigurationBuilder().Build();
        var allowExperimental = configuration.GetValue("Agctor:AllowExperimentalRuntimes", false);
        runtimeName = AgctorRuntimeCatalog.NormalizeRuntimeName(
            runtimeName ?? configuration["Agctor:DefaultRuntime"]);

        if (AgctorRuntimeCatalog.IsExperimental(runtimeName) && !allowExperimental)
            runtimeName = AgctorRuntimeCatalog.InMemory;

        services.AddAgctor(options =>
        {
            options.DefaultRuntime = runtimeName;
            options.AllowExperimentalRuntimes = allowExperimental;
            options.MaxConcurrentMessages = 100;
            options.EnableDetailedLogging = false;
            options.Environment = "CLI";
            configureOptions?.Invoke(options);
        });

        services.AddAgctorToolCatalog(configuration);
        services.AddCodeGraphGeneration();
        services.AddAgctorActivityTracking();
        services.AddHttpClient<AgctorSDK.CodeGraph.Llm.OllamaLlmClient>();
        services.AddSingleton<AgctorSDK.CodeGraph.Llm.ILlmClient>(
            sp => sp.GetRequiredService<AgctorSDK.CodeGraph.Llm.OllamaLlmClient>());

        return services;
    }
}
