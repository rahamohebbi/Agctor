namespace AgctorSDK.Core.Rag.Ingest;

/// <summary>Runtime options passed to an ingest source when enumerating documents.</summary>
public sealed record RagIngestSourceContext(
    string ProjectRoot,
    string? CollectionId = null,
    IReadOnlyDictionary<string, string>? Options = null);

/// <summary>One logical document produced by a source and sent to <see cref="IRagProviderAdapter.IngestAsync"/>.</summary>
public sealed record RagIngestDocument(
    string RelativePath,
    string Content,
    string? CollectionId = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>Non-destructive scan before a batch ingest run.</summary>
public sealed record RagIngestSourcePreview(
    int DocumentCount,
    IReadOnlyList<string> SamplePaths,
    string Message,
    int DatasetBatchCount = 0);

/// <summary>Outcome for a single document ingest attempt.</summary>
public sealed record RagIngestItemResult(
    string RelativePath,
    bool Success,
    string Message,
    string? DocumentId = null);

/// <summary>Batch ingest summary for dashboard feedback.</summary>
public sealed record RagIngestBatchResult(
    bool Success,
    string ProviderId,
    string SourceId,
    int TotalDocuments,
    int Succeeded,
    int Failed,
    IReadOnlyList<RagIngestItemResult> Items,
    string Message);

/// <summary>Catalog row for ingest UI — implemented sources run; others show as planned.</summary>
public sealed record RagIngestSourceDescriptor(
    string Id,
    string DisplayName,
    string Description,
    bool IsImplemented);
