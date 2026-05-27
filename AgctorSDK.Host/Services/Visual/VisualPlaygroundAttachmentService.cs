using System.Linq;
using System.Text.Json;
using AgctorSDK.Core.ProjectMemory.Visual;
using AgctorSDK.Core.ProjectMemory.Visual.Models;
using AgctorSDK.Core.ProjectMemory.Visual.Storage;
using AgctorSDK.Core.Sessions.Models;
using AgctorSDK.Host.Models;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Host.Services.Visual;

/// <summary>Links uploaded visual assets to a playground turn and emits SSE-friendly attachment payloads.</summary>
public sealed class VisualPlaygroundAttachmentService
{
    private readonly VisualAssetCatalogStore _catalog;
    private readonly IBlobStore _blobs;
    private readonly IVisualPipelineService _pipeline;
    private readonly VisualStorageOptions _options;

    public VisualPlaygroundAttachmentService(
        VisualAssetCatalogStore catalog,
        IBlobStore blobs,
        IVisualPipelineService pipeline,
        IOptions<VisualStorageOptions> options)
    {
        _catalog = catalog;
        _blobs = blobs;
        _pipeline = pipeline;
        _options = options?.Value ?? new VisualStorageOptions();
    }

    public async Task<IReadOnlyList<SessionAttachmentRef>> LinkAndEnrichAsync(
        string projectRoot,
        string scenarioId,
        string sessionId,
        string turnGroupId,
        IReadOnlyList<PlaygroundStreamAttachmentDto>? attachmentRefs,
        string? userMessage = null,
        string? focusEntityKey = null,
        bool queueBackgroundExtract = true,
        CancellationToken cancellationToken = default)
    {
        var result = new List<SessionAttachmentRef>();
        var linkedIds = new List<string>();
        var byAssetId = (attachmentRefs ?? Array.Empty<PlaygroundStreamAttachmentDto>())
            .Where(a => !string.IsNullOrWhiteSpace(a.AssetId))
            .GroupBy(a => a.AssetId.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var pair in byAssetId)
        {
            var id = pair.Key;
            var dto = pair.Value;
            var record = await _catalog.LoadAsync(projectRoot, scenarioId, id, cancellationToken).ConfigureAwait(false);
            if (record == null)
                continue;

            record.UploadedBySessionId = sessionId;
            record.SourceTurnGroupId = turnGroupId;

            ApplyManualAnnotation(record, dto);

            VisualMessageIdentityHints.TryApplyToRecord(record, userMessage, focusEntityKey, projectRoot, scenarioId);
            await _catalog.SaveAsync(projectRoot, scenarioId, record, cancellationToken).ConfigureAwait(false);

            var att = new SessionAttachmentRef
            {
                AssetId = id,
                Kind = "image",
                Mime = record.Storage.ContentType,
                State = record.State,
                Caption = string.IsNullOrWhiteSpace(dto.Caption) ? record.Context.UserCaption : dto.Caption.Trim()
            };

            if (!string.Equals(record.State, VisualAssetStates.PendingUpload, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(record.State, VisualAssetStates.Deleted, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (string.Equals(_options.Provider, "file", StringComparison.OrdinalIgnoreCase))
                    {
                        att.ViewUrl = VisualAssetViewUrls.Build(id, scenarioId, projectRoot);
                        att.ViewUrlExpiresAt = DateTimeOffset.UtcNow.AddSeconds(
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
                        att.ViewUrl = access.Url;
                        att.ViewUrlExpiresAt = access.ExpiresAt;
                    }
                }
                catch
                {
                    // preview is best-effort during stream
                }
            }

            if (record.Subjects.Count > 0)
            {
                att.EntityKeys = record.Subjects
                    .Select(s => s.EntityKey)
                    .Where(k => !string.IsNullOrWhiteSpace(k))
                    .ToList();
            }

            result.Add(att);
            linkedIds.Add(id);
        }

        if (linkedIds.Count > 0 && queueBackgroundExtract)
        {
            _pipeline.QueueExtractForAssets(projectRoot, scenarioId, linkedIds, userMessage, focusEntityKey);
        }

        return result;
    }

    public static string SerializeSsePayload(object payload) =>
        JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    private static void ApplyManualAnnotation(VisualAssetRecord record, PlaygroundStreamAttachmentDto dto)
    {
        var subjects = new List<VisualAssetSubject>();
        if (!string.IsNullOrWhiteSpace(dto.EntityKey))
        {
            subjects.Add(new VisualAssetSubject
            {
                EntityKey = dto.EntityKey.Trim(),
                Role = "primary",
                DisplayName = FormatDisplayName(dto.EntityKey.Trim())
            });
        }

        if (!string.IsNullOrWhiteSpace(dto.SecondaryEntityKey))
        {
            subjects.Add(new VisualAssetSubject
            {
                EntityKey = dto.SecondaryEntityKey.Trim(),
                Role = "also_in_photo",
                DisplayName = FormatDisplayName(dto.SecondaryEntityKey.Trim())
            });
        }

        if (subjects.Count > 0)
        {
            record.Subjects = subjects;
            record.Inference ??= new VisualAssetInference();
            record.Inference.Source = "manual_tag";
            record.Inference.Confidence = 0.95;
            record.Inference.EntityKeys = subjects.Select(s => s.EntityKey).ToList();
            record.Inference.Rationale = "Tagged in composer before send.";
            if (string.Equals(record.State, VisualAssetStates.Uploaded, StringComparison.OrdinalIgnoreCase))
                record.State = VisualAssetStates.ReadyForExtract;
        }

        if (!string.IsNullOrWhiteSpace(dto.Caption))
            record.Context.UserCaption = dto.Caption.Trim();

        if (!string.IsNullOrWhiteSpace(dto.Sensitivity))
            record.Privacy.Sensitivity = dto.Sensitivity.Trim();
    }

    private static string FormatDisplayName(string entityKey) =>
        entityKey.Length == 1
            ? entityKey.ToUpperInvariant()
            : char.ToUpperInvariant(entityKey[0]) + entityKey[1..];
}
