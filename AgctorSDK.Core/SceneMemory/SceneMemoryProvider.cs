using System;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Rag;

namespace AgctorSDK.Core.SceneMemory;

/// <summary>Provider adapter over an independent scene store, shared by HTTP and agent tools.</summary>
public sealed class SceneMemoryProvider : ISceneMemoryProvider
{
    private readonly ISceneMemoryStore _store;

    /// <summary>Creates a provider over any registered store implementation.</summary>
    public SceneMemoryProvider(ISceneMemoryStore store) => _store = store ?? throw new ArgumentNullException(nameof(store));

    /// <inheritdoc />
    public string ProviderId => RagProviderIds.SceneMemory;

    /// <inheritdoc />
    public async Task<RagHealthResult> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            // Reading the store verifies readiness without creating a workspace or requiring a model.
            await _store.ListAsync(cancellationToken).ConfigureAwait(false);
            return new(RagHealthStatus.Healthy, "Scene Memory is ready; manual state and history retrieval.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new(RagHealthStatus.Unavailable, $"Scene Memory storage is unavailable: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public Task<SceneQueryResult> QueryAsync(SceneMemoryQueryRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.WorkspaceId)) throw new ArgumentException("workspaceId is required.");
        if (string.IsNullOrWhiteSpace(request.EntityId) && string.IsNullOrWhiteSpace(request.Query))
            throw new ArgumentException("query or entityId is required.");
        return _store.QueryAsync(request.WorkspaceId,
            new SceneQuery(request.EntityId, request.Query, request.AtSequence), cancellationToken);
    }
}
