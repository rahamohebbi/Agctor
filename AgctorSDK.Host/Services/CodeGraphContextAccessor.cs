using AgctorSDK.Host.Models;

namespace AgctorSDK.Host.Services;

/// <summary>
/// In-memory holder for the current CodeGraph context (PRD-006). Thread-safe for single writer (scenario), multiple readers (API).
/// </summary>
public class CodeGraphContextAccessor : ICodeGraphContextAccessor
{
    private volatile CodeGraphContextDto? _current;
    private Func<CancellationToken, Task<int>>? _embeddingCountProvider;
    private Func<CancellationToken, Task<IReadOnlyList<EmbeddingRecordDto>>>? _embeddingRecordsProvider;

    public CodeGraphContextDto? GetCurrent() => _current;

    public async Task<CodeGraphContextDto?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var ctx = _current;
        if (ctx == null) return null;
        if (_embeddingCountProvider == null) return ctx;
        var count = await _embeddingCountProvider(cancellationToken);
        return new CodeGraphContextDto
        {
            ActorTree = ctx.ActorTree,
            EmbeddingStoreSummary = new EmbeddingStoreSummaryDto { VectorCount = count }
        };
    }

    public void SetCurrent(CodeGraphContextDto? context) => _current = context;

    public void SetEmbeddingCountProvider(Func<CancellationToken, Task<int>>? provider) => _embeddingCountProvider = provider;

    public void SetEmbeddingRecordsProvider(Func<CancellationToken, Task<IReadOnlyList<EmbeddingRecordDto>>>? provider) =>
        _embeddingRecordsProvider = provider;

    public async Task<IReadOnlyList<EmbeddingRecordDto>> GetEmbeddingRecordsAsync(CancellationToken cancellationToken = default)
    {
        if (_embeddingRecordsProvider == null) return Array.Empty<EmbeddingRecordDto>();
        return await _embeddingRecordsProvider(cancellationToken);
    }
}
