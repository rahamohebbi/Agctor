using AgctorSDK.Core.Rag;
using AgctorSDK.Core.Rag.Transport;
using AgctorSDK.Core.Rag.Ingest;
using AgctorSDK.Extensions.Rag;
using AgctorSDK.Extensions.Rag.Ingest;
using AgctorSDK.Extensions.Rag.Providers;
using AgctorSDK.Extensions.Rag.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgctorSDK.Extensions.DependencyInjection;

/// <summary>Registers external RAG provider adapters and factory (PRD-025).</summary>
public static class RagServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="IRagProviderAdapterFactory"/> and v1 provider adapters (None, LightRAG, Graphiti, Cognee).
    /// </summary>
    public static IServiceCollection AddAgctorRagProviders(
        this IServiceCollection services,
        IConfiguration? configuration = null,
        Action<RagOptions>? configureOptions = null)
    {
        if (configuration != null)
            services.Configure<RagOptions>(configuration.GetSection("Agctor:Rag"));

        if (configureOptions != null)
            services.Configure(configureOptions);

        services.AddHttpClient<RestRagTransport>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(2);
        });
        services.AddHttpClient<McpHttpRagTransport>(client =>
        {
            // Cognee remember/cognify can run LLM graph extraction for several minutes per dataset batch.
            client.Timeout = TimeSpan.FromMinutes(30);
        });
        services.TryAddSingleton<IRestRagTransport>(sp => sp.GetRequiredService<RestRagTransport>());
        services.TryAddSingleton<IMcpHttpRagTransport>(sp => sp.GetRequiredService<McpHttpRagTransport>());

        services.TryAddSingleton<NullRagProviderAdapter>();
        services.TryAddSingleton<LightRagProviderAdapter>();
        services.TryAddSingleton<GraphitiProviderAdapter>();
        services.TryAddSingleton<CogneeProviderAdapter>();
        services.TryAddSingleton<IRagProviderAdapterFactory, RagProviderAdapterFactory>();
        services.TryAddSingleton<AgctorSDK.Core.ProjectMemory.Rag.RagContextService>();

        services.TryAddSingleton<IRagIngestSource, AgctorMarkdownIngestSource>();
        services.TryAddSingleton<IRagIngestSourceRegistry, RagIngestSourceRegistry>();
        services.TryAddSingleton<RagIngestOrchestrator>();

        return services;
    }
}
