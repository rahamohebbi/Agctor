using System;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Visual.Models;
using AgctorSDK.Core.ProjectMemory.Visual.Storage;

namespace AgctorSDK.Core.ProjectMemory.Visual;

/// <summary>Tombstones a visual asset catalog entry and deletes its blob (PRD-023).</summary>
public sealed class VisualAssetDeleter
{
    private readonly VisualAssetCatalogStore _catalog;
    private readonly IBlobStore _blobs;

    public VisualAssetDeleter(VisualAssetCatalogStore catalog, IBlobStore blobs)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _blobs = blobs ?? throw new ArgumentNullException(nameof(blobs));
    }

    public async Task<VisualAssetDeleteResult> DeleteAsync(
        string projectRoot,
        string scenarioId,
        string assetId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = assetId?.Trim();
        if (string.IsNullOrWhiteSpace(id))
            return VisualAssetDeleteResult.Fail("asset_id_required");

        var scenario = PersonaScenarioScope.SanitizeFolderSegment(scenarioId);
        var record = await _catalog.LoadAsync(projectRoot, scenario, id, cancellationToken).ConfigureAwait(false);
        if (record == null)
            return VisualAssetDeleteResult.Fail("asset_not_found");

        if (string.Equals(record.State, VisualAssetStates.Deleted, StringComparison.OrdinalIgnoreCase))
            return VisualAssetDeleteResult.Ok(id, blobDeleted: false, alreadyDeleted: true);

        var blobDeleted = await TryDeleteBlobAsync(record, cancellationToken).ConfigureAwait(false);
        record.State = VisualAssetStates.Deleted;
        record.Extraction.Status = "deleted";
        await _catalog.SaveAsync(projectRoot, scenario, record, cancellationToken).ConfigureAwait(false);

        return VisualAssetDeleteResult.Ok(id, blobDeleted, alreadyDeleted: false);
    }

    private async Task<bool> TryDeleteBlobAsync(VisualAssetRecord record, CancellationToken cancellationToken)
    {
        var bucket = record.Storage?.Bucket?.Trim();
        var key = record.Storage?.Key?.Trim();
        if (string.IsNullOrWhiteSpace(bucket) || string.IsNullOrWhiteSpace(key))
            return false;

        try
        {
            await _blobs.DeleteObjectAsync(bucket, key, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

public sealed class VisualAssetDeleteResult
{
    public bool Success { get; init; }

    public string? AssetId { get; init; }

    public bool BlobDeleted { get; init; }

    public bool AlreadyDeleted { get; init; }

    public string? Error { get; init; }

    public static VisualAssetDeleteResult Ok(string assetId, bool blobDeleted, bool alreadyDeleted) =>
        new()
        {
            Success = true,
            AssetId = assetId,
            BlobDeleted = blobDeleted,
            AlreadyDeleted = alreadyDeleted
        };

    public static VisualAssetDeleteResult Fail(string error) =>
        new() { Success = false, Error = error };
}
