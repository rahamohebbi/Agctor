using AgctorSDK.Core.Ollama;
using AgctorSDK.Core.ProjectMemory.Visual;
using AgctorSDK.Core.ProjectMemory.Visual.Models;
using AgctorSDK.Core.ProjectMemory.Visual.Storage;

namespace AgctorSDK.Host.Services.ProjectMemory;

/// <summary>
/// Person-query photo answers: use stored scene summaries first; run vision when catalog text is still thin.
/// </summary>
public static class PlaygroundPersonQueryVisionHelper
{
    public static async Task<string?> DescribePrimaryAssetAsync(
        IOllamaVisionChatClient vision,
        VisualAssetCatalogStore catalog,
        IBlobStore blobs,
        string projectRoot,
        string scenarioId,
        IReadOnlyList<string> assetIds,
        string? userMessage,
        string? focusEntityKey,
        CancellationToken cancellationToken)
    {
        if (assetIds == null || assetIds.Count == 0)
            return null;

        var assetId = assetIds[0];
        var record = await catalog
            .LoadAsync(projectRoot, scenarioId, assetId.Trim(), cancellationToken)
            .ConfigureAwait(false);
        if (record?.Storage == null
            || string.IsNullOrWhiteSpace(record.Storage.Bucket)
            || string.IsNullOrWhiteSpace(record.Storage.Key))
            return null;

        byte[] bytes;
        try
        {
            bytes = await blobs
                .ReadObjectBytesAsync(record.Storage.Bucket, record.Storage.Key, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            return null;
        }

        if (bytes.Length == 0)
            return null;

        var base64 = Convert.ToBase64String(bytes);
        var chat = await vision
            .ChatAsync(
                VisualExtractPrompts.QuerySceneSystemPrompt,
                VisualExtractPrompts.BuildQuerySceneUserText(record, userMessage, focusEntityKey),
                new[] { base64 },
                numPredict: 256,
                cancellationToken)
            .ConfigureAwait(false);

        if (!chat.Success || string.IsNullOrWhiteSpace(chat.Content))
            return null;

        var summary = VisualSceneSummary.Normalize(chat.Content);
        if (VisualSceneSummary.IsUseful(summary))
        {
            await VisualCatalogSceneSummaryWriter
                .PersistAsync(
                    catalog,
                    projectRoot,
                    scenarioId,
                    assetId,
                    summary!,
                    chat.ModelUsed ?? "vision-query",
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return summary;
    }
}
