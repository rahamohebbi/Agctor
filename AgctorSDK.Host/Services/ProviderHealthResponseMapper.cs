using AgctorSDK.Core.Rag;
using AgctorSDK.Host.Models;

namespace AgctorSDK.Host.Services;

/// <summary>Shared health response mapping for document retrieval and Scene Memory.</summary>
public static class ProviderHealthResponseMapper
{
    /// <summary>Preserves the existing RAG health response shape for both provider families.</summary>
    public static RagProviderHealthResponseDto Map(string providerId, RagHealthResult health) => new()
    {
        ProviderId = providerId,
        OverallStatus = health.Status == RagHealthStatus.Healthy ? "healthy" : "degraded",
        ProviderHealthStatus = health.Status.ToString(),
        Detail = health.Message
    };
}
