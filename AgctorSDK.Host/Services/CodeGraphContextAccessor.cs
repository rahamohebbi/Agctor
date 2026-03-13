using AgctorSDK.CodeGraph.Persistence;
using AgctorSDK.Host.Models;

namespace AgctorSDK.Host.Services;

/// <summary>
/// In-memory holder for the current CodeGraph context (PRD-006). Thread-safe for single writer (scenario), multiple readers (API).
/// </summary>
public class CodeGraphContextAccessor : ICodeGraphContextAccessor
{
    private volatile CodeGraphContextDto? _current;
    private Func<ActorSerializer.ActorDto?>? _actorTreeProvider;
    private Func<CancellationToken, Task<int>>? _embeddingCountProvider;
    private Func<CancellationToken, Task<EmbeddingStoreSummaryDto>>? _embeddingSummaryProvider;
    private Func<CancellationToken, Task<IReadOnlyList<EmbeddingRecordDto>>>? _embeddingRecordsProvider;

    public CodeGraphContextDto? GetCurrent()
    {
        var ctx = _current;
        if (ctx == null) return null;
        var tree = _actorTreeProvider != null ? _actorTreeProvider() : ctx.ActorTree;
        var summary = ctx.EmbeddingStoreSummary;
        return new CodeGraphContextDto
        {
            ActorTree = tree ?? ctx.ActorTree,
            EmbeddingStoreSummary = summary == null
                ? null
                : new EmbeddingStoreSummaryDto
                {
                    VectorCount = summary.VectorCount,
                    State = summary.State,
                    IsReady = summary.IsReady,
                    GraphVersion = summary.GraphVersion,
                    IndexedGraphVersion = summary.IndexedGraphVersion,
                    LastIndexedAt = summary.LastIndexedAt,
                    LastError = summary.LastError
                }
        };
    }

    public async Task<CodeGraphContextDto?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var ctx = _current;
        if (ctx == null) return null;
        var tree = _actorTreeProvider != null ? _actorTreeProvider() : ctx.ActorTree;
        var summary = _embeddingSummaryProvider != null
            ? await _embeddingSummaryProvider(cancellationToken)
            : new EmbeddingStoreSummaryDto
            {
                VectorCount = _embeddingCountProvider != null
                    ? await _embeddingCountProvider(cancellationToken)
                    : (ctx.EmbeddingStoreSummary?.VectorCount ?? 0),
                State = ctx.EmbeddingStoreSummary?.State ?? "NotReady",
                IsReady = ctx.EmbeddingStoreSummary?.IsReady ?? false,
                GraphVersion = ctx.EmbeddingStoreSummary?.GraphVersion ?? 0,
                IndexedGraphVersion = ctx.EmbeddingStoreSummary?.IndexedGraphVersion ?? 0,
                LastIndexedAt = ctx.EmbeddingStoreSummary?.LastIndexedAt,
                LastError = ctx.EmbeddingStoreSummary?.LastError
            };

        return new CodeGraphContextDto
        {
            ActorTree = tree ?? ctx.ActorTree,
            EmbeddingStoreSummary = summary
        };
    }

    public void SetCurrent(CodeGraphContextDto? context) => _current = context;

    public void SetActorTreeProvider(Func<ActorSerializer.ActorDto?>? provider) => _actorTreeProvider = provider;

    public void SetEmbeddingCountProvider(Func<CancellationToken, Task<int>>? provider) => _embeddingCountProvider = provider;

    public void SetEmbeddingSummaryProvider(Func<CancellationToken, Task<EmbeddingStoreSummaryDto>>? provider) =>
        _embeddingSummaryProvider = provider;

    public void SetEmbeddingRecordsProvider(Func<CancellationToken, Task<IReadOnlyList<EmbeddingRecordDto>>>? provider) =>
        _embeddingRecordsProvider = provider;

    public async Task<IReadOnlyList<EmbeddingRecordDto>> GetEmbeddingRecordsAsync(CancellationToken cancellationToken = default)
    {
        if (_embeddingRecordsProvider == null) return Array.Empty<EmbeddingRecordDto>();
        return await _embeddingRecordsProvider(cancellationToken);
    }
}
