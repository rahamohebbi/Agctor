namespace AgctorSDK.Core.Rag;

/// <summary>Semantic query passed to any <see cref="IRagProviderAdapter"/>.</summary>
public sealed record RagQueryRequest(
    string Query,
    string? CollectionId,
    int TopK = 8,
    string? FilterJson = null,
    RagQueryMode Mode = RagQueryMode.Auto);

/// <summary>Normalized retrieval result for Project Memory appendix builders.</summary>
public sealed record RagQueryResult(
    IReadOnlyList<RagContextChunk> Chunks,
    string? ProviderTraceId = null,
    string? RawDebugJson = null);

/// <summary>One retrieved context unit with optional provenance.</summary>
public sealed record RagContextChunk(
    string Text,
    double? Score = null,
    string? SourcePath = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>Document or corpus ingest request (optional in v1 dashboards).</summary>
public sealed record RagIngestRequest(
    string SourcePath,
    string? CollectionId = null,
    string? Content = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>Ingest outcome for operator feedback.</summary>
public sealed record RagIngestResult(
    bool Success,
    string Message,
    string? DocumentId = null);

/// <summary>Provider health probe for dashboard and fallback logic.</summary>
public sealed record RagHealthResult(
    RagHealthStatus Status,
    string Message,
    string? ProviderVersion = null);
