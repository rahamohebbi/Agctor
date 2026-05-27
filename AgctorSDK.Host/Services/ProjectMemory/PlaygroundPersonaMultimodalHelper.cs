using AgctorSDK.Core.Ollama;
using AgctorSDK.Core.ProjectMemory.Visual;
using AgctorSDK.Core.ProjectMemory.Visual.Storage;

namespace AgctorSDK.Host.Services.ProjectMemory;

/// <summary>Multimodal playground persona calls when the current turn includes photos (023e coaches).</summary>
public static class PlaygroundPersonaMultimodalHelper
{
    public static bool ShouldUseVision(string personaId, bool hasAttachments)
    {
        if (!hasAttachments)
            return false;
        return string.Equals(personaId, "style-coach", StringComparison.OrdinalIgnoreCase)
               || string.Equals(personaId, "fitness-coach", StringComparison.OrdinalIgnoreCase)
               || string.Equals(personaId, "visual-intake", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<IReadOnlyList<string>> LoadTurnImagesBase64Async(
        VisualAssetCatalogStore catalog,
        IBlobStore blobs,
        string projectRoot,
        string scenarioId,
        IReadOnlyList<string> assetIds,
        CancellationToken cancellationToken)
    {
        var images = new List<string>();
        foreach (var assetId in assetIds)
        {
            if (string.IsNullOrWhiteSpace(assetId))
                continue;
            var record = await catalog
                .LoadAsync(projectRoot, scenarioId, assetId.Trim(), cancellationToken)
                .ConfigureAwait(false);
            if (record?.Storage == null
                || string.IsNullOrWhiteSpace(record.Storage.Bucket)
                || string.IsNullOrWhiteSpace(record.Storage.Key))
                continue;

            try
            {
                var bytes = await blobs
                    .ReadObjectBytesAsync(record.Storage.Bucket, record.Storage.Key, cancellationToken)
                    .ConfigureAwait(false);
                if (bytes.Length > 0)
                    images.Add(Convert.ToBase64String(bytes));
            }
            catch
            {
                // skip unreadable blob
            }
        }

        return images;
    }

    public static Task<OllamaVisionChatResult> RunVisionPersonaAsync(
        IOllamaVisionChatClient vision,
        string systemPrompt,
        string userText,
        IReadOnlyList<string> base64Images,
        CancellationToken cancellationToken) =>
        vision.ChatAsync(systemPrompt, userText, base64Images, numPredict: 512, cancellationToken);
}
