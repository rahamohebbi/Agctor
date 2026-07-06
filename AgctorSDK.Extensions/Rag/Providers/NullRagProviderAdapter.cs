namespace AgctorSDK.Extensions.Rag.Providers;

using AgctorSDK.Core.Rag;

/// <summary>
/// No external RAG — callers fall back to on-disk markdown strategies (PRD-025).
/// </summary>
public sealed class NullRagProviderAdapter : IRagProviderAdapter
{
    /// <inheritdoc />
    public string ProviderId => RagProviderIds.None;

    /// <inheritdoc />
    public Task<RagHealthResult> GetHealthAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new RagHealthResult(
            RagHealthStatus.Healthy,
            "Markdown-only mode; no external RAG sidecar."));

    /// <inheritdoc />
    public Task<RagQueryResult> QueryAsync(RagQueryRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new RagQueryResult(Array.Empty<RagContextChunk>()));

    /// <inheritdoc />
    public Task<RagIngestResult> IngestAsync(RagIngestRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new RagIngestResult(
            true,
            "Ingest skipped — None provider uses canonical files under .agctor/ only."));
}
