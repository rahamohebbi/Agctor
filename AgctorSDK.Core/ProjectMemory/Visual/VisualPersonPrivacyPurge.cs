using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Visual.Models;
using AgctorSDK.Core.ProjectMemory.Visual.Storage;

namespace AgctorSDK.Core.ProjectMemory.Visual;

/// <summary>Deletes scenario visual assets (YAML + blob) that reference a person entity key.</summary>
public sealed class VisualPersonPrivacyPurge : IVisualPersonPrivacyPurge
{
    private readonly VisualAssetCatalogStore _catalog;
    private readonly IBlobStore _blobs;

    public VisualPersonPrivacyPurge(VisualAssetCatalogStore catalog, IBlobStore blobs)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _blobs = blobs ?? throw new ArgumentNullException(nameof(blobs));
    }

    public async Task<VisualPersonPurgeResult> PurgePersonAsync(
        string projectRoot,
        string scenarioId,
        string entityKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = PersonaScenarioScope.SanitizeFolderSegment(entityKey);
        if (string.IsNullOrWhiteSpace(key))
            return new VisualPersonPurgeResult();

        var scenarioSeg = PersonaScenarioScope.SanitizeFolderSegment(scenarioId);
        var assets = await _catalog.ListAsync(projectRoot, scenarioSeg, cancellationToken).ConfigureAwait(false);
        var matches = assets.Where(a => AssetReferencesEntity(a, key)).ToList();
        var blobsDeleted = 0;
        var assetsRemoved = 0;

        foreach (var record in matches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await TryDeleteBlobAsync(record, cancellationToken).ConfigureAwait(false))
                blobsDeleted++;

            var yamlPath = VisualAssetPaths.AssetCatalogPath(projectRoot, scenarioSeg, record.AssetId);
            if (File.Exists(yamlPath))
            {
                File.Delete(yamlPath);
                assetsRemoved++;
            }
        }

        return new VisualPersonPurgeResult
        {
            AssetsRemoved = assetsRemoved,
            BlobsDeleted = blobsDeleted
        };
    }

    private async Task<bool> TryDeleteBlobAsync(VisualAssetRecord record, CancellationToken cancellationToken)
    {
        var bucket = record.Storage?.Bucket?.Trim();
        var blobKey = record.Storage?.Key?.Trim();
        if (string.IsNullOrWhiteSpace(bucket) || string.IsNullOrWhiteSpace(blobKey))
            return false;

        try
        {
            await _blobs.DeleteObjectAsync(bucket, blobKey, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static bool AssetReferencesEntity(VisualAssetRecord record, string entityKey) =>
        record.Subjects.Any(s =>
            string.Equals(
                PersonaScenarioScope.SanitizeFolderSegment(s.EntityKey),
                entityKey,
                StringComparison.OrdinalIgnoreCase));
}
