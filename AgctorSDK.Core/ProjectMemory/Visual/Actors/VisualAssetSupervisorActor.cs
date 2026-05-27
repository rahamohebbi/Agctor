using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.ProjectMemory.Visual.Models;
using AgctorSDK.Core.ProjectMemory.Visual.Storage;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Core.ProjectMemory.Visual.Actors;

/// <summary>Validates uploads and maintains the on-disk visual asset catalog (PRD-023a).</summary>
public sealed class VisualAssetSupervisorActor : IActor
{
    private readonly VisualAssetCatalogStore _catalog;
    private readonly IBlobStore _blobs;
    private readonly VisualStorageOptions _options;
    private ActorState _state = ActorState.Initializing;

    public VisualAssetSupervisorActor(
        string id,
        VisualAssetCatalogStore catalog,
        IBlobStore blobs,
        IOptions<VisualStorageOptions> options)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("Actor id is required.", nameof(id)) : id;
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _blobs = blobs ?? throw new ArgumentNullException(nameof(blobs));
        _options = options?.Value ?? new VisualStorageOptions();
    }

    public string Id { get; }
    public string ActorType => nameof(VisualAssetSupervisorActor);
    public ActorState State => _state;
    public event EventHandler<ActorStateChangedEventArgs>? StateChanged;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ChangeState(ActorState.Active, "Initialized");
        return Task.CompletedTask;
    }

    public async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        try
        {
            return envelope.Payload switch
            {
                VisualAssetInitUploadRequest init => AgctorEnvelopeBuilder.Response(
                    await HandleInitAsync(init, cancellationToken).ConfigureAwait(false),
                    envelope,
                    Id,
                    AgctorMessageTypes.Result),
                VisualAssetCompleteUploadRequest complete => AgctorEnvelopeBuilder.Response(
                    await HandleCompleteAsync(complete, cancellationToken).ConfigureAwait(false),
                    envelope,
                    Id,
                    AgctorMessageTypes.Result),
                _ => AgctorEnvelopeBuilder.Error(
                    envelope,
                    Id,
                    $"Unsupported visual asset payload '{envelope.Payload?.GetType().Name ?? "null"}'.")
            };
        }
        catch (Exception ex)
        {
            return AgctorEnvelopeBuilder.Error(envelope, Id, ex.Message, ex);
        }
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        ChangeState(ActorState.Stopped, "Shutdown");
        return Task.CompletedTask;
    }

    private async Task<VisualAssetInitUploadResult> HandleInitAsync(
        VisualAssetInitUploadRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectRoot))
            return FailInit("project_root_required");
        if (string.IsNullOrWhiteSpace(request.ScenarioId))
            return FailInit("scenario_id_required");
        if (request.Bytes <= 0)
            return FailInit("bytes_required");
        if (request.Bytes > _options.MaxUploadBytes)
            return FailInit($"file_exceeds_max_bytes ({_options.MaxUploadBytes})");

        var mime = request.ContentType?.Trim().ToLowerInvariant() ?? "";
        if (!_options.AllowedMimeTypes.Any(t => string.Equals(t, mime, StringComparison.OrdinalIgnoreCase)))
            return FailInit($"content_type_not_allowed: {mime}");

        var root = Path.GetFullPath(request.ProjectRoot.Trim());
        var projectId = VisualAssetPaths.ResolveProjectId(root);
        var scenarioSeg = PersonaScenarioScope.SanitizeFolderSegment(request.ScenarioId);
        var assetId = Guid.NewGuid().ToString("N");
        var ext = VisualAssetPaths.ExtensionForMime(mime);
        var key = VisualAssetPaths.BlobKey(projectId, scenarioSeg, assetId, ext);
        var bucket = _options.Bucket;

        var expiry = TimeSpan.FromSeconds(Math.Max(60, _options.PresignedUploadExpirySeconds));
        var presigned = await _blobs.CreatePresignedUploadAsync(
            bucket,
            key,
            mime,
            request.Bytes,
            expiry,
            cancellationToken).ConfigureAwait(false);

        var record = new VisualAssetRecord
        {
            AssetId = assetId,
            ScenarioId = scenarioSeg,
            ProjectId = projectId,
            State = VisualAssetStates.PendingUpload,
            UploadedAt = DateTimeOffset.UtcNow,
            UploadedBySessionId = string.IsNullOrWhiteSpace(request.SessionId) ? null : request.SessionId.Trim(),
            SourceTurnGroupId = string.IsNullOrWhiteSpace(request.TurnGroupId) ? null : request.TurnGroupId.Trim(),
            Storage = new VisualAssetStorageRef
            {
                Bucket = bucket,
                Key = key,
                ContentType = mime,
                Bytes = request.Bytes
            }
        };

        await _catalog.SaveAsync(root, scenarioSeg, record, cancellationToken).ConfigureAwait(false);

        return new VisualAssetInitUploadResult(
            true,
            assetId,
            presigned.UploadUrl,
            presigned.UploadHeaders,
            presigned.ExpiresAt,
            null);
    }

    private async Task<VisualAssetCompleteUploadResult> HandleCompleteAsync(
        VisualAssetCompleteUploadRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectRoot))
            return FailComplete("project_root_required");
        if (string.IsNullOrWhiteSpace(request.ScenarioId))
            return FailComplete("scenario_id_required");
        if (string.IsNullOrWhiteSpace(request.AssetId))
            return FailComplete("asset_id_required");

        var root = Path.GetFullPath(request.ProjectRoot.Trim());
        var scenarioSeg = PersonaScenarioScope.SanitizeFolderSegment(request.ScenarioId);
        var record = await _catalog.LoadAsync(root, scenarioSeg, request.AssetId.Trim(), cancellationToken)
            .ConfigureAwait(false);
        if (record == null)
            return FailComplete("asset_not_found");

        try
        {
            await _blobs.VerifyUploadedAsync(
                record.Storage.Bucket,
                record.Storage.Key,
                request.Sha256Hex,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            record.State = VisualAssetStates.Failed;
            await _catalog.SaveAsync(root, scenarioSeg, record, cancellationToken).ConfigureAwait(false);
            return FailComplete(ex.Message);
        }

        record.State = VisualAssetStates.Uploaded;
        record.Storage.Sha256 = string.IsNullOrWhiteSpace(request.Sha256Hex) ? record.Storage.Sha256 : request.Sha256Hex.Trim();
        record.Extraction.Status = "pending";
        await _catalog.SaveAsync(root, scenarioSeg, record, cancellationToken).ConfigureAwait(false);

        return new VisualAssetCompleteUploadResult(true, record, null);
    }

    private static VisualAssetInitUploadResult FailInit(string error) =>
        new(false, null, null, null, null, error);

    private static VisualAssetCompleteUploadResult FailComplete(string error) =>
        new(false, null, error);

    private void ChangeState(ActorState newState, string reason)
    {
        if (_state == newState)
            return;
        var old = _state;
        _state = newState;
        StateChanged?.Invoke(this, new ActorStateChangedEventArgs(old, newState, reason));
    }
}
