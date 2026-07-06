namespace AgctorSDK.Core.Rag;

/// <summary>
/// Transport-agnostic contract for external RAG backends (LightRAG, Cognee, future SaaS).
/// Implementations live in Extensions; callers depend only on this interface (PRD-025).
/// </summary>
public interface IRagProviderAdapter
{
    /// <summary>Catalog id: None, LightRAG, Cognee, …</summary>
    string ProviderId { get; }

    /// <summary>Probe sidecar or remote API readiness.</summary>
    Task<RagHealthResult> GetHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>Retrieve ranked context chunks for a natural-language query.</summary>
    Task<RagQueryResult> QueryAsync(RagQueryRequest request, CancellationToken cancellationToken = default);

    /// <summary>Index or refresh a document in the provider corpus.</summary>
    Task<RagIngestResult> IngestAsync(RagIngestRequest request, CancellationToken cancellationToken = default);
}
