namespace AgctorSDK.Core.ProjectMemory.Rag;

/// <summary>Correlation carried explicitly across actor mailboxes.</summary>
public sealed record RagInspectionContext(string SessionId, string TurnGroupId, string? TraceId = null,
    string? NodeId = null, string? AgentId = null, string? ProviderSource = null);

public static class RagInspectionScope
{
    private static readonly AsyncLocal<RagInspectionContext?> Slot = new();
    public static RagInspectionContext? Current => Slot.Value;
    public static IDisposable Begin(RagInspectionContext? context)
    {
        var previous = Slot.Value;
        Slot.Value = context;
        return new Restore(() => Slot.Value = previous);
    }
    private sealed class Restore(Action restore) : IDisposable { public void Dispose() => restore(); }
}

public sealed record RagInspectionStage(string Name, string Status, double DurationMs, string? Detail = null);
public sealed record RagInspectionChunk(int Rank, string Text, string? Source, double? Score,
    IReadOnlyDictionary<string, string>? Metadata, bool PreviewTruncated = false);

public sealed class RagInspectionRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required RagInspectionContext Correlation { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public string? ScenarioId { get; set; }
    public string ProviderId { get; set; } = "None";
    public string ProviderSource { get; set; } = "global default";
    public string Strategy { get; set; } = "";
    public string Outcome { get; set; } = "running";
    public string? Reason { get; set; }
    public bool MarkdownFallback { get; set; }
    public double DurationMs { get; set; }
    public string Query { get; set; } = "";
    public string? CollectionId { get; set; }
    public string Mode { get; set; } = "Auto";
    public int TopK { get; set; }
    public int ResultCount { get; set; }
    public List<RagInspectionChunk> Results { get; set; } = [];
    public bool ResultsPreviewTruncated { get; set; }
    public string PreparedContext { get; set; } = "";
    public bool ContextTruncated { get; set; }
    public bool ContextPreviewTruncated { get; set; }
    public string? ProviderTraceId { get; set; }
    public string? RawResponseJson { get; set; }
    public bool RawResponseTruncated { get; set; }
    public List<RagInspectionStage> Stages { get; set; } = [];
}

/// <summary>Lightweight list entry; full content is loaded only when a record is opened.</summary>
public sealed record RagInspectionSummary(string Id, RagInspectionContext Correlation, DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt, string? ScenarioId, string ProviderId, string ProviderSource, string Strategy,
    string Outcome, string? Reason, bool MarkdownFallback, double DurationMs, int ResultCount);

public interface IRagInspectionStore
{
    Task SaveRagInspectionAsync(RagInspectionRecord record, CancellationToken ct = default);
    Task<IReadOnlyList<RagInspectionSummary>> GetRagInspectionsAsync(string sessionId, string? turnGroupId = null,
        string? traceId = null, CancellationToken ct = default);
    Task<RagInspectionRecord?> GetRagInspectionAsync(string sessionId, string id, CancellationToken ct = default);
}
