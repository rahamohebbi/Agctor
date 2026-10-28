namespace AgctorSDK.Core.Rag;

/// <summary>
/// Transport-agnostic contract for external RAG backends (LightRAG, Cognee, future SaaS).
/// Implementations live in Extensions; callers depend only on this interface (PRD-025).
/// </summary>
public interface IRagProviderAdapter : AgctorSDK.Core.Retrieval.IRetrievalProvider<RagQueryRequest, RagQueryResult>
{
    /// <summary>Index or refresh a document in the provider corpus.</summary>
    Task<RagIngestResult> IngestAsync(RagIngestRequest request, CancellationToken cancellationToken = default);
}
