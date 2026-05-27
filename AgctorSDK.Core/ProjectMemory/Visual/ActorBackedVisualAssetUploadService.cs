using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Visual.Models;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.ProjectMemory.Visual.Actors;
using AgctorSDK.Core.ProjectMemory.Visual.Storage;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Core.ProjectMemory.Visual;

/// <summary>Routes visual upload workflows through <see cref="VisualAssetSupervisorActor"/>.</summary>
public sealed class ActorBackedVisualAssetUploadService : IVisualAssetUploadService
{
    private const string ActorId = "visual:asset-supervisor";
    private const string SenderId = "visual-upload-facade";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(5);

    private readonly IActorRuntimeAdapter _runtime;
    private readonly VisualAssetCatalogStore _catalog;
    private readonly IBlobStore _blobs;
    private readonly IOptions<VisualStorageOptions> _options;
    private readonly SemaphoreSlim _spawnLock = new(1, 1);

    public ActorBackedVisualAssetUploadService(
        IActorRuntimeAdapter runtime,
        VisualAssetCatalogStore catalog,
        IBlobStore blobs,
        IOptions<VisualStorageOptions> options)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _blobs = blobs ?? throw new ArgumentNullException(nameof(blobs));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<VisualAssetInitUploadResult> InitUploadAsync(
        VisualAssetInitUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureActorAsync(cancellationToken).ConfigureAwait(false);
        return await SendAsync<VisualAssetInitUploadResult>(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<VisualAssetCompleteUploadResult> CompleteUploadAsync(
        VisualAssetCompleteUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureActorAsync(cancellationToken).ConfigureAwait(false);
        return await SendAsync<VisualAssetCompleteUploadResult>(request, cancellationToken).ConfigureAwait(false);
    }

    public Task<Models.VisualAssetRecord?> GetAssetAsync(
        string projectRoot,
        string scenarioId,
        string assetId,
        CancellationToken cancellationToken = default) =>
        _catalog.LoadAsync(projectRoot, scenarioId, assetId, cancellationToken);

    public Task<IReadOnlyList<Models.VisualAssetRecord>> ListAssetsAsync(
        string projectRoot,
        string scenarioId,
        CancellationToken cancellationToken = default) =>
        _catalog.ListAsync(projectRoot, scenarioId, cancellationToken);

    private async Task<TResponse> SendAsync<TResponse>(object payload, CancellationToken cancellationToken)
        where TResponse : class
    {
        var headers = new Dictionary<string, string>
        {
            [AgctorMessageHeaders.MessageType] = payload.GetType().Name
        };

        return await _runtime.SendMessageAsync<TResponse>(
            ActorId,
            payload,
            RequestTimeout,
            SenderId,
            headers,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureActorAsync(CancellationToken cancellationToken)
    {
        if (await _runtime.GetActorAsync<VisualAssetSupervisorActor>(ActorId, cancellationToken).ConfigureAwait(false) != null)
            return;

        await _spawnLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (await _runtime.GetActorAsync<VisualAssetSupervisorActor>(ActorId, cancellationToken).ConfigureAwait(false) == null)
            {
                var catalog = _catalog;
                var blobs = _blobs;
                var options = _options;
                await _runtime.SpawnActorAsync(
                    ActorId,
                    id => new VisualAssetSupervisorActor(id, catalog, blobs, options),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _spawnLock.Release();
        }
    }
}
