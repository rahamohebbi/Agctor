using System.Linq;
using System.Text.Json;
using AgctorSDK.Core.ProjectMemory.Visual;
using AgctorSDK.Core.ProjectMemory.Visual.Models;
using AgctorSDK.Core.ProjectMemory.Visual.Storage;
using AgctorSDK.Core.Sessions;
using AgctorSDK.Core.Sessions.Models;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Host.Services.Visual;

/// <summary>Adds signed view URLs to session turn attachments for the playground transcript.</summary>
public sealed class VisualTranscriptEnricher
{
    private readonly VisualAssetCatalogStore _catalog;
    private readonly IBlobStore _blobs;
    private readonly VisualStorageOptions _options;

    public VisualTranscriptEnricher(
        VisualAssetCatalogStore catalog,
        IBlobStore blobs,
        IOptions<VisualStorageOptions> options)
    {
        _catalog = catalog;
        _blobs = blobs;
        _options = options?.Value ?? new VisualStorageOptions();
    }

    public async Task EnrichTurnsAsync(
        string projectRoot,
        string? scenarioId,
        IReadOnlyList<SessionTurn> turns,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectRoot) || string.IsNullOrWhiteSpace(scenarioId))
            return;

        foreach (var turn in turns)
        {
            var env = SessionAttachmentJson.Deserialize(turn.AttachmentsJson);
            if (env == null || env.Attachments.Count == 0)
                continue;

            turn.Attachments = new List<SessionAttachmentRef>();
            foreach (var att in env.Attachments)
            {
                var copy = new SessionAttachmentRef
                {
                    AssetId = att.AssetId,
                    Kind = att.Kind,
                    Mime = att.Mime,
                    FileName = att.FileName,
                    State = att.State,
                    Caption = att.Caption
                };

                var record = await _catalog.LoadAsync(projectRoot, scenarioId, att.AssetId, cancellationToken)
                    .ConfigureAwait(false);
                if (record != null
                    && !string.Equals(record.State, VisualAssetStates.Deleted, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(record.State, VisualAssetStates.PendingUpload, StringComparison.OrdinalIgnoreCase))
                {
                    copy.State = record.State;
                    try
                    {
                        // Browsers cannot load file:// from the playground origin; always proxy via Host.
                        if (string.Equals(_options.Provider, "file", StringComparison.OrdinalIgnoreCase))
                        {
                            copy.ViewUrl = VisualAssetViewUrls.Build(att.AssetId, scenarioId, projectRoot);
                            copy.ViewUrlExpiresAt = DateTimeOffset.UtcNow.AddSeconds(
                                Math.Max(60, _options.PresignedViewExpirySeconds));
                        }
                        else
                        {
                            var expiry = TimeSpan.FromSeconds(Math.Max(60, _options.PresignedViewExpirySeconds));
                            var access = await _blobs.CreatePresignedGetAsync(
                                record.Storage.Bucket,
                                record.Storage.Key,
                                expiry,
                                cancellationToken).ConfigureAwait(false);
                            copy.ViewUrl = access.Url;
                            copy.ViewUrlExpiresAt = access.ExpiresAt;
                        }
                    }
                    catch
                    {
                        // best-effort thumbnail in transcript
                    }
                }

                if (record != null && record.Subjects.Count > 0)
                {
                    copy.EntityKeys = record.Subjects
                        .Select(s => s.EntityKey)
                        .Where(k => !string.IsNullOrWhiteSpace(k))
                        .ToList();
                }

                copy.StatusDetail = VisualAssetStatusDetail.ForRecord(record);

                turn.Attachments.Add(copy);
            }
        }
    }
}
