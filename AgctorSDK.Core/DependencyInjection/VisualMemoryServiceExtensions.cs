using System;
using AgctorSDK.Core.Ollama;
using AgctorSDK.Core.ProjectMemory.Visual;
using AgctorSDK.Core.ProjectMemory.Visual.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgctorSDK.Core.DependencyInjection;

public static class VisualMemoryServiceExtensions
{
    /// <summary>PRD-023: visual blob store, asset catalog, upload + vision pipeline actors.</summary>
    public static IServiceCollection AddAgctorVisualMemory(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<VisualStorageOptions>(configuration.GetSection("Agctor:Visual"));
        services.Configure<LlmVisionOptions>(configuration.GetSection("Agctor:LLM"));

        var llm = configuration.GetSection("Agctor:LLM");
        var defaultModel = llm.GetValue<string>("DefaultModel");
        var visionModel = llm.GetValue<string>("VisionModel");
        var fallbacks = llm.GetSection("VisionFallbackModels").Get<string[]>() ?? Array.Empty<string>();
        var visionTimeout = llm.GetValue<int?>("VisionTimeoutSeconds");
        OllamaRuntimeConfiguration.ConfigureVision(visionModel ?? defaultModel, fallbacks, visionTimeout);

        var provider = configuration.GetValue<string>("Agctor:Visual:Provider")?.Trim().ToLowerInvariant() ?? "s3";
        if (string.Equals(provider, "file", StringComparison.Ordinal))
            services.AddSingleton<IBlobStore, FileSystemBlobStore>();
        else
            services.AddSingleton<IBlobStore, S3CompatibleBlobStore>();

        services.AddHttpClient<IOllamaVisionChatClient, OllamaVisionChatClient>();
        services.AddSingleton<VisualAssetCatalogStore>();
        services.AddSingleton<PersonVisualContextBuilder>();
        services.AddSingleton<VisualPipelineService>();
        services.AddSingleton<ActorBackedVisualAssetUploadService>();
        services.AddSingleton<IVisualAssetUploadService>(sp => sp.GetRequiredService<ActorBackedVisualAssetUploadService>());
        services.AddSingleton<ActorBackedVisualPipelineService>();
        services.AddSingleton<IVisualPipelineService>(sp =>
        {
            var options = configuration.GetValue<string>("Agctor:Visual:UseActorPipeline");
            if (string.Equals(options, "false", StringComparison.OrdinalIgnoreCase))
                return sp.GetRequiredService<VisualPipelineService>();
            return sp.GetRequiredService<ActorBackedVisualPipelineService>();
        });

        // PRD-023f: forget-person removes catalog YAML + blob keys for that entity.
        services.AddSingleton<IVisualPersonPrivacyPurge, VisualPersonPrivacyPurge>();

        return services;
    }
}
