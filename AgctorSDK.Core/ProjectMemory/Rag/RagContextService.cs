using System.Text;
using AgctorSDK.Core.Rag;

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
public sealed class RagContextService
{
    private readonly IRagProviderAdapterFactory _factory;

    public RagContextService(IRagProviderAdapterFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>Queries the configured provider and formats retrieved chunks for prompt injection.</summary>
    public async Task<RagContextAppendixResult> BuildAppendixAsync(
        RagContextRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserMessage))
            return RagContextAppendixResult.Empty;

        var providerId = string.IsNullOrWhiteSpace(request.ProviderId)
            ? _factory.GetDefaultProviderId()
            : RagProviderIds.Normalize(request.ProviderId);

        if (string.Equals(providerId, RagProviderIds.None, StringComparison.Ordinal))
            return RagContextAppendixResult.Empty;

        IRagProviderAdapter adapter;
        try
        {
            adapter = _factory.CreateProvider(providerId);
        }
        catch (Exception ex)
        {
            return RagContextAppendixResult.Fallback(ex.Message, providerId);
        }

        var health = await adapter.GetHealthAsync(cancellationToken).ConfigureAwait(false);
        if (health.Status is not RagHealthStatus.Healthy and not RagHealthStatus.Degraded)
            return RagContextAppendixResult.Fallback(health.Message, providerId);

        RagQueryResult query;
        try
        {
            query = await adapter.QueryAsync(
                new RagQueryRequest(
                    request.UserMessage.Trim(),
                    request.CollectionId,
                    request.TopK,
                    Mode: request.Mode),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return RagContextAppendixResult.Fallback(ex.Message, providerId);
        }

        if (query.Chunks.Count == 0)
            return RagContextAppendixResult.Fallback("RAG provider returned no chunks.", providerId);

        var body = FormatChunks(query.Chunks, request.MaxAppendixChars);
        var appendix = new StringBuilder();
        appendix.AppendLine("---");
        appendix.AppendLine($"External RAG context ({providerId}, read-only):");
        appendix.AppendLine(body);
        return new RagContextAppendixResult(appendix.ToString().TrimEnd(), true, false, providerId);
    }

    /// <summary>Formats retrieved chunks into markdown sections.</summary>
    public static string FormatChunks(IReadOnlyList<RagContextChunk> chunks, int maxChars)
    {
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
                sb.Length = maxChars;
                sb.AppendLine("\n(RAG context truncated.)");
                break;
            }
        }

        return sb.ToString().TrimEnd();
    }
}
