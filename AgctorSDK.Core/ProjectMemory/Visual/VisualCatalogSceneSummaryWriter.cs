using System;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Visual.Models;

namespace AgctorSDK.Core.ProjectMemory.Visual;

/// <summary>Persists query-time scene descriptions back onto catalog assets for later text-only queries.</summary>
public static class VisualCatalogSceneSummaryWriter
{
    public static async Task PersistAsync(
        VisualAssetCatalogStore catalog,
        string projectRoot,
        string scenarioId,
        string assetId,
        string sceneSummary,
        string sourceModel,
        CancellationToken cancellationToken = default)
    {
        var normalized = VisualSceneSummary.Normalize(sceneSummary);
        if (!VisualSceneSummary.IsUseful(normalized))
            return;

        var record = await catalog
            .LoadAsync(projectRoot, scenarioId, assetId.Trim(), cancellationToken)
            .ConfigureAwait(false);
        if (record == null)
            return;

        record.Extraction.SceneSummary = normalized;
        if (string.IsNullOrWhiteSpace(record.Extraction.Status)
            || string.Equals(record.Extraction.Status, "pending", StringComparison.OrdinalIgnoreCase))
            record.Extraction.Status = "completed";
        record.Extraction.OllamaModel = sourceModel;
        record.Extraction.PromptVersion = VisualExtractPrompts.QuerySceneVersion;
        record.Extraction.LastRunAt = DateTimeOffset.UtcNow;
        await catalog.SaveAsync(projectRoot, scenarioId, record, cancellationToken).ConfigureAwait(false);
    }
}
