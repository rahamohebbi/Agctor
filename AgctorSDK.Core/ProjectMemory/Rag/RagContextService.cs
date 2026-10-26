using System.Text;
using AgctorSDK.Core.Rag;
using AgctorSDK.Core.Ollama;

namespace AgctorSDK.Core.ProjectMemory.Rag;

/// <summary>Input for <see cref="RagContextService"/> appendix building.</summary>
public sealed record RagContextRequest(
    string UserMessage,
    string? ProviderId = null,
    string? CollectionId = null,
    RagQueryMode Mode = RagQueryMode.Auto,
    int TopK = 8,
    int MaxAppendixChars = 120_000);

/// <summary>Outcome of external RAG context retrieval for LLM prompts.</summary>
public sealed record RagContextAppendixResult(
    string Appendix,
    bool UsedExternalRag,
    bool FellBack,
    string? ProviderId = null,
    string? FallbackReason = null)
{
    /// <summary>Empty appendix — caller should use markdown strategies.</summary>
    public static RagContextAppendixResult Empty { get; } = new("", false, false);

    public static RagContextAppendixResult Fallback(string reason, string? providerId = null) =>
        new("", false, true, providerId, reason);
}

/// <summary>
/// Orchestrates <see cref="IRagProviderAdapter"/> queries into a markdown appendix block (PRD-025 Phase 2).
/// </summary>
public sealed partial class RagContextService
{
    private readonly IRagProviderAdapterFactory _factory;

    public RagContextService(IRagProviderAdapterFactory factory, IRagInspectionStore? inspectionStore = null,
        AgctorSDK.Core.Utils.ActivityTracking.IActivityTracker? activityTracker = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _inspectionStore = inspectionStore;
        _activityTracker = activityTracker;
    }

    /// <summary>Queries the configured provider and formats retrieved chunks for prompt injection.</summary>
    public async Task<RagContextAppendixResult> BuildAppendixAsync(
        RagContextRequest request,
        CancellationToken cancellationToken = default)
    {
        var inspection = ActiveInspection.Value;
        var selection = ResolveProvider(request.ProviderId);
        var providerId = selection.ProviderId;
        if (inspection != null)
        {
            inspection.ProviderId = providerId;
            inspection.ProviderSource = selection.Source;
            inspection.Query = request.UserMessage.Trim();
            inspection.CollectionId = request.CollectionId;
            inspection.Mode = request.Mode.ToString();
            inspection.TopK = request.TopK;
        }
        RagContextAppendixResult Fallback(string outcome, string reason)
        {
            if (inspection != null) { inspection.Outcome = outcome; inspection.Reason = reason; }
            return RagContextAppendixResult.Fallback(reason, providerId);
        }
        if (string.IsNullOrWhiteSpace(request.UserMessage))
            return Fallback("skipped", "Search text was empty.");
        if (providerId == RagProviderIds.None)
            return Fallback("disabled", "External retrieval is disabled by the selected provider.");

        try
        {
            var adapter = _factory.CreateProvider(providerId);
            var health = await InspectStageAsync("Health check", () => adapter.GetHealthAsync(cancellationToken));
            if (health.Status is not RagHealthStatus.Healthy and not RagHealthStatus.Degraded)
                return Fallback("unavailable", health.Message);
            var query = await InspectStageAsync("Searching", () => adapter.QueryAsync(
                new RagQueryRequest(request.UserMessage.Trim(), request.CollectionId, request.TopK, Mode: request.Mode), cancellationToken));
            if (inspection != null)
            {
                inspection.ResultCount = query.Chunks.Count;
                inspection.ProviderTraceId = query.ProviderTraceId;
                inspection.RawResponseJson = SafeRawJson(query.RawDebugJson, out var rawTruncated);
                inspection.RawResponseTruncated = rawTruncated;
                var budget = 256_000;
                foreach (var chunk in query.Chunks.Take(100))
                {
                    var length = Math.Min(chunk.Text.Length, Math.Min(20_000, budget));
                    if (length <= 0) break;
                    inspection.Results.Add(new(inspection.Results.Count + 1, chunk.Text[..length], chunk.SourcePath,
                        chunk.Score, SafeMetadata(chunk.Metadata), length < chunk.Text.Length));
                    budget -= length;
                }
                inspection.ResultsPreviewTruncated = inspection.Results.Count < query.Chunks.Count || inspection.Results.Any(c => c.PreviewTruncated);
                inspection.Stages.Add(new("Results received", "complete", 0, $"{query.Chunks.Count} result(s)"));
                await SaveInspectionAsync(inspection);
            }
            if (!string.IsNullOrWhiteSpace(query.Error))
                return Fallback("failed", query.Error);
            if (query.Chunks.Count == 0)
                return Fallback("no_results", "RAG provider returned no results.");
            var body = FormatChunks(query.Chunks, request.MaxAppendixChars, out var truncated);
            if (inspection != null) inspection.ContextTruncated = truncated;
            var appendix = $"---\nExternal RAG context ({providerId}, read-only):\n{body}";
            if (inspection != null) inspection.Outcome = "success";
            return new RagContextAppendixResult(appendix.TrimEnd(), true, false, providerId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { return Fallback("failed", ex.Message); }
    }

    /// <summary>Formats retrieved chunks into markdown sections.</summary>
    public static string FormatChunks(IReadOnlyList<RagContextChunk> chunks, int maxChars) => FormatChunks(chunks, maxChars, out _);

    private static string FormatChunks(IReadOnlyList<RagContextChunk> chunks, int maxChars, out bool truncated)
    {
        truncated = false;
        var sb = new StringBuilder();
        var i = 0;
        foreach (var chunk in chunks)
        {
            i++;
            var header = chunk.SourcePath != null ? $"[{i}] {chunk.SourcePath}" : $"[{i}]";
            if (chunk.Score is { } score)
                header += $" (score {score:F3})";

            sb.AppendLine($"### {header}");
            sb.AppendLine(chunk.Text.Trim());
            sb.AppendLine();

            if (sb.Length >= maxChars)
            {
                truncated = true;
                sb.Length = maxChars;
                sb.AppendLine("\n(RAG context truncated.)");
                break;
            }
        }

        return sb.ToString().TrimEnd();
    }
}
