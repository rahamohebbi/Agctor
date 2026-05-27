using AgctorSDK.Core.ProjectMemory.Visual;
using AgctorSDK.Core.ProjectMemory.Visual.Models;
using AgctorSDK.Host.Models;

namespace AgctorSDK.Host.Services.Visual;

/// <summary>Links pending inbox rows to recent visual assets for playground thumbnails.</summary>
public sealed class GenericInboxVisualEnricher
{
    private readonly VisualAssetCatalogStore _catalog;

    public GenericInboxVisualEnricher(VisualAssetCatalogStore catalog)
    {
        _catalog = catalog;
    }

    public async Task EnrichWithSourceAssetsAsync(
        string projectRoot,
        string scenarioId,
        IList<GenericInboxPendingItemDto> items,
        CancellationToken cancellationToken = default)
    {
        if (items.Count == 0)
            return;

        var assets = await _catalog.ListAsync(projectRoot, scenarioId, cancellationToken).ConfigureAwait(false);
        var visualByEntity = assets
            .Where(a => string.Equals(a.State, VisualAssetStates.InboxPending, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(a.State, VisualAssetStates.Extracted, StringComparison.OrdinalIgnoreCase))
            .SelectMany(a => a.Subjects.Select(s => (Entity: s.EntityKey, Asset: a)))
            .Where(x => !string.IsNullOrWhiteSpace(x.Entity))
            .GroupBy(x => x.Entity.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Asset.AssetId, StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.EntityKey))
                continue;
            if (visualByEntity.TryGetValue(item.EntityKey.Trim(), out var assetId))
                item.SourceAssetId = assetId;
        }
    }
}
