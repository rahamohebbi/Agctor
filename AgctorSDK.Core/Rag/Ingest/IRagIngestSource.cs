namespace AgctorSDK.Core.Rag.Ingest;

/// <summary>
/// Pluggable document producer for RAG sidecar ingest (markdown today; PDF/folder later).
/// Each implementation lives in Extensions; orchestrator fans out to <see cref="IRagProviderAdapter"/>.
/// </summary>
public interface IRagIngestSource
{
    /// <summary>Matches <see cref="RagIngestSourceIds"/>.</summary>
    string SourceId { get; }

    /// <summary>Count/sample paths without calling the provider.</summary>
    Task<RagIngestSourcePreview> PreviewAsync(
        RagIngestSourceContext context,
        CancellationToken cancellationToken = default);

    /// <summary>Yield documents to ingest; paths are relative to <see cref="RagIngestSourceContext.ProjectRoot"/>.</summary>
    IAsyncEnumerable<RagIngestDocument> EnumerateDocumentsAsync(
        RagIngestSourceContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>Resolves implemented ingest sources by id.</summary>
public interface IRagIngestSourceRegistry
{
    IReadOnlyList<RagIngestSourceDescriptor> ListCatalog();

    IRagIngestSource? TryGetImplemented(string sourceId);
}
