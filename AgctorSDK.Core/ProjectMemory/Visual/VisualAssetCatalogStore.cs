using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Visual.Models;
using AgctorSDK.Core.ProjectMemory.Yaml;

namespace AgctorSDK.Core.ProjectMemory.Visual;

/// <summary>Reads/writes per-asset YAML under <c>scenarios/&lt;id&gt;/visual/assets/</c>.</summary>
public sealed class VisualAssetCatalogStore
{
    public async Task<VisualAssetRecord?> LoadAsync(
        string projectRoot,
        string scenarioId,
        string assetId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = VisualAssetPaths.AssetCatalogPath(projectRoot, scenarioId, assetId);
        if (!File.Exists(path))
            return null;

        var text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(text)
            ? null
            : ProjectYamlSerializer.Deserialize<VisualAssetRecord>(text);
    }

    public async Task SaveAsync(
        string projectRoot,
        string scenarioId,
        VisualAssetRecord record,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (record == null || string.IsNullOrWhiteSpace(record.AssetId))
            throw new ArgumentException("Asset record with assetId is required.", nameof(record));

        var folder = VisualAssetPaths.AssetsFolder(projectRoot, scenarioId);
        Directory.CreateDirectory(folder);
        var path = VisualAssetPaths.AssetCatalogPath(projectRoot, scenarioId, record.AssetId);
        var yaml = ProjectYamlSerializer.Serialize(record);
        await File.WriteAllTextAsync(path, yaml, cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<VisualAssetRecord>> ListAsync(
        string projectRoot,
        string scenarioId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var folder = VisualAssetPaths.AssetsFolder(projectRoot, scenarioId);
        if (!Directory.Exists(folder))
            return Task.FromResult<IReadOnlyList<VisualAssetRecord>>(Array.Empty<VisualAssetRecord>());

        var list = new List<VisualAssetRecord>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.yaml"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var text = File.ReadAllText(file);
                var record = ProjectYamlSerializer.Deserialize<VisualAssetRecord>(text);
                if (record != null && !string.Equals(record.State, VisualAssetStates.Deleted, StringComparison.OrdinalIgnoreCase))
                    list.Add(record);
            }
            catch
            {
                // skip corrupt catalog files
            }
        }

        return Task.FromResult<IReadOnlyList<VisualAssetRecord>>(list);
    }
}
