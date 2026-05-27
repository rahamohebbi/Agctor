using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Ollama;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Orchestration;
using AgctorSDK.Core.ProjectMemory.OutOfSchema;
using AgctorSDK.Core.ProjectMemory.Processing;
using AgctorSDK.Core.ProjectMemory.Visual.Actors;
using AgctorSDK.Core.ProjectMemory.Visual.Models;
using AgctorSDK.Core.ProjectMemory.Visual.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Core.ProjectMemory.Visual;

/// <summary>Gemma 4 vision infer/extract orchestration shared by visual pipeline actors.</summary>
public sealed class VisualPipelineService : IVisualPipelineService
{
    private readonly VisualAssetCatalogStore _catalog;
    private readonly IBlobStore _blobs;
    private readonly IOllamaVisionChatClient _vision;
    private readonly IProjectLoader _projectLoader;
    private readonly IMemoryIntentProcessor _processor;
    private readonly IGenericInboxStore _inbox;
    private readonly LlmVisionOptions _visionOptions;
    private readonly ILogger<VisualPipelineService> _logger;

    public VisualPipelineService(
        VisualAssetCatalogStore catalog,
        IBlobStore blobs,
        IOllamaVisionChatClient vision,
        IProjectLoader projectLoader,
        IMemoryIntentProcessor processor,
        IGenericInboxStore inbox,
        IOptions<LlmVisionOptions> visionOptions,
        ILogger<VisualPipelineService> logger)
    {
        _catalog = catalog;
        _blobs = blobs;
        _vision = vision;
        _projectLoader = projectLoader;
        _processor = processor;
        _inbox = inbox;
        _visionOptions = visionOptions?.Value ?? new LlmVisionOptions();
        _logger = logger;
    }

    public async Task<VisualIngestEnrichResult> EnrichIngestAsync(
        VisualIngestEnrichRequest request,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(request.ProjectRoot.Trim());
        var scenario = PersonaScenarioScope.SanitizeFolderSegment(request.ScenarioId);
        var record = await _catalog.LoadAsync(root, scenario, request.AssetId.Trim(), cancellationToken)
            .ConfigureAwait(false);
        if (record == null)
            return new VisualIngestEnrichResult { Success = false, Error = "asset_not_found" };

        if (record.CapturedAt == null)
        {
            try
            {
                if (await _blobs.ObjectExistsAsync(record.Storage.Bucket, record.Storage.Key, cancellationToken)
                        .ConfigureAwait(false))
                {
                    var bytes = await _blobs
                        .ReadObjectBytesAsync(record.Storage.Bucket, record.Storage.Key, cancellationToken)
                        .ConfigureAwait(false);
                    record.Storage.Bytes = bytes.Length;
                }
            }
            catch
            {
                // best-effort byte count
            }

            record.CapturedAt = DateTimeOffset.UtcNow;
            await _catalog.SaveAsync(root, scenario, record, cancellationToken).ConfigureAwait(false);
        }

        return new VisualIngestEnrichResult
        {
            Success = true,
            CapturedAt = record.CapturedAt
        };
    }

    public async Task<VisualInferResult> InferFromPromptAsync(
        VisualInferRequest request,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(request.ProjectRoot.Trim());
        var scenario = PersonaScenarioScope.SanitizeFolderSegment(request.ScenarioId);
        var record = await _catalog.LoadAsync(root, scenario, request.AssetId.Trim(), cancellationToken)
            .ConfigureAwait(false);
        if (record == null)
            return new VisualInferResult { Success = false, Error = "asset_not_found" };

        if (ShouldSkipVision(record))
        {
            return new VisualInferResult
            {
                Success = true,
                Skipped = true,
                Record = record
            };
        }

        record.State = VisualAssetStates.Inferring;
        await _catalog.SaveAsync(root, scenario, record, cancellationToken).ConfigureAwait(false);

        try
        {
            var base64 = await LoadImageBase64Async(record, cancellationToken).ConfigureAwait(false);
            var chat = await _vision.ChatAsync(
                    VisualExtractPrompts.InferSystemPrompt,
                    VisualExtractPrompts.BuildInferUserText(record, request.UserMessage, request.FocusEntityKey),
                    new[] { base64 },
                    numPredict: Math.Min(128, _visionOptions.VisualTokenBudget),
                    cancellationToken)
                .ConfigureAwait(false);

            if (!chat.Success)
            {
                if (VisualMessageIdentityHints.TryApplyToRecord(
                        record,
                        request.UserMessage,
                        request.FocusEntityKey,
                        root,
                        scenario))
                {
                    await _catalog.SaveAsync(root, scenario, record, cancellationToken).ConfigureAwait(false);
                    _logger.LogInformation(
                        "Visual infer used caption heuristics for {AssetId} (Ollama unavailable: {Error})",
                        record.AssetId,
                        chat.Error);
                    return new VisualInferResult
                    {
                        Success = true,
                        ModelUsed = "caption-heuristic",
                        Record = record
                    };
                }

                record.State = VisualAssetStates.Failed;
                await _catalog.SaveAsync(root, scenario, record, cancellationToken).ConfigureAwait(false);
                return new VisualInferResult { Success = false, Error = chat.Error, Record = record };
            }

            if (!VisualVisionInferPayload.TryParse(chat.Content, out var infer, out var parseErr))
            {
                record.State = VisualAssetStates.Failed;
                await _catalog.SaveAsync(root, scenario, record, cancellationToken).ConfigureAwait(false);
                return new VisualInferResult { Success = false, Error = parseErr, Record = record };
            }

            ApplyInferPayload(record, infer!, request.FocusEntityKey);
            record.State = VisualAssetStates.ReadyForExtract;
            await _catalog.SaveAsync(root, scenario, record, cancellationToken).ConfigureAwait(false);

            return new VisualInferResult
            {
                Success = true,
                ModelUsed = chat.ModelUsed,
                Record = record
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Visual infer failed for asset {AssetId}", record.AssetId);
            record.State = VisualAssetStates.Failed;
            await _catalog.SaveAsync(root, scenario, record, cancellationToken).ConfigureAwait(false);
            return new VisualInferResult { Success = false, Error = ex.Message, Record = record };
        }
    }

    public async Task<VisualExtractResult> ExtractAsync(
        VisualExtractRequest request,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(request.ProjectRoot.Trim());
        var scenario = PersonaScenarioScope.SanitizeFolderSegment(request.ScenarioId);
        var record = await _catalog.LoadAsync(root, scenario, request.AssetId.Trim(), cancellationToken)
            .ConfigureAwait(false);
        if (record == null)
            return new VisualExtractResult { Success = false, Error = "asset_not_found" };

        if (ShouldSkipVision(record))
        {
            return new VisualExtractResult
            {
                Success = true,
                Skipped = true,
                Record = record
            };
        }

        record.State = VisualAssetStates.Extracting;
        record.Extraction.Status = "running";
        record.Extraction.LastRunAt = DateTimeOffset.UtcNow;
        record.Extraction.PromptVersion = VisualExtractPrompts.ExtractVersion;
        await _catalog.SaveAsync(root, scenario, record, cancellationToken).ConfigureAwait(false);

        try
        {
            var base64 = await LoadImageBase64Async(record, cancellationToken).ConfigureAwait(false);
            var chat = await _vision.ChatAsync(
                    VisualExtractPrompts.ExtractSystemPrompt,
                    VisualExtractPrompts.BuildExtractUserText(record, request.UserMessage, request.FocusEntityKey),
                    new[] { base64 },
                    numPredict: _visionOptions.VisualTokenBudget,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!chat.Success)
            {
                record.State = VisualAssetStates.Failed;
                record.Extraction.Status = "failed";
                await _catalog.SaveAsync(root, scenario, record, cancellationToken).ConfigureAwait(false);
                return new VisualExtractResult { Success = false, Error = chat.Error, Record = record };
            }

            if (!MemoryIntentJson.TryParseBatch(chat.Content, out var batch, out var parseErr, out _))
            {
                record.State = VisualAssetStates.Failed;
                record.Extraction.Status = "failed";
                await _catalog.SaveAsync(root, scenario, record, cancellationToken).ConfigureAwait(false);
                return new VisualExtractResult { Success = false, Error = parseErr, Record = record };
            }

            var sceneSummary = VisualSceneSummary.TryParseFromExtractJson(chat.Content)
                               ?? VisualSceneSummary.BuildFromIntents(batch!.MemoryIntents);
            if (VisualSceneSummary.IsUseful(sceneSummary))
                record.Extraction.SceneSummary = sceneSummary;

            var ctx = await _projectLoader.LoadAsync(root, cancellationToken).ConfigureAwait(false);
            var routed = _processor.Route(ctx, batch!.MemoryIntents, out var routeIssues);
            var proposals = OutOfSchemaProposalFactory
                .FromRouteIssues(routeIssues, ctx.Runtime.OutOfSchema)
                .ToList();

            if (proposals.Count > 0)
            {
                await _inbox.AppendPendingAsync(root, scenario, proposals, cancellationToken).ConfigureAwait(false);
                record.State = VisualAssetStates.InboxPending;
            }
            else if (routed.Count > 0)
            {
                record.State = VisualAssetStates.Extracted;
            }
            else
            {
                record.State = VisualAssetStates.Ready;
            }

            record.Extraction.Status = "completed";
            record.Extraction.OllamaModel = chat.ModelUsed;
            record.Extraction.PromptVersion = VisualExtractPrompts.ExtractVersion;
            record.Extraction.LastRunAt = DateTimeOffset.UtcNow;
            await _catalog.SaveAsync(root, scenario, record, cancellationToken).ConfigureAwait(false);

            return new VisualExtractResult
            {
                Success = true,
                ModelUsed = chat.ModelUsed,
                IntentCount = batch.MemoryIntents.Count,
                ProposalCount = proposals.Count,
                RoutedCount = routed.Count,
                Record = record,
                Proposals = proposals
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Visual extract failed for asset {AssetId}", record.AssetId);
            record.State = VisualAssetStates.Failed;
            record.Extraction.Status = "failed";
            await _catalog.SaveAsync(root, scenario, record, cancellationToken).ConfigureAwait(false);
            return new VisualExtractResult { Success = false, Error = ex.Message, Record = record };
        }
    }

    private async Task<string> LoadImageBase64Async(VisualAssetRecord record, CancellationToken cancellationToken)
    {
        var bytes = await _blobs
            .ReadObjectBytesAsync(record.Storage.Bucket, record.Storage.Key, cancellationToken)
            .ConfigureAwait(false);
        return await VisualImageEncoder
            .ToBase64JpegAsync(bytes, _visionOptions.MaxVisualEdgePixels, cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool ShouldSkipVision(VisualAssetRecord record) =>
        string.Equals(record.Privacy.Sensitivity, "do_not_infer", StringComparison.OrdinalIgnoreCase);

    public void QueueExtractForAssets(
        string projectRoot,
        string scenarioId,
        IReadOnlyList<string> assetIds,
        string? userMessage,
        string? focusEntityKey)
    {
        if (assetIds == null || assetIds.Count == 0)
            return;

        _ = Task.Run(async () =>
        {
            foreach (var assetId in assetIds)
            {
                if (string.IsNullOrWhiteSpace(assetId))
                    continue;

                try
                {
                    await InferFromPromptAsync(new VisualInferRequest
                    {
                        ProjectRoot = projectRoot,
                        ScenarioId = scenarioId,
                        AssetId = assetId.Trim(),
                        UserMessage = userMessage,
                        FocusEntityKey = focusEntityKey
                    }, CancellationToken.None).ConfigureAwait(false);

                    await ExtractAsync(new VisualExtractRequest
                    {
                        ProjectRoot = projectRoot,
                        ScenarioId = scenarioId,
                        AssetId = assetId.Trim(),
                        UserMessage = userMessage,
                        FocusEntityKey = focusEntityKey
                    }, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Background visual extract failed for {AssetId}", assetId);
                }
            }
        });
    }

    private static void ApplyInferPayload(VisualAssetRecord record, VisualVisionInferPayload infer, string? focusEntityKey)
    {
        var keys = infer.EntityKeys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => PersonaScenarioScope.SanitizeFolderSegment(k).ToLowerInvariant())
            .Where(k => !FocusEntityPolicy.IsPlaceholderSlug(k))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (keys.Count == 0 && !string.IsNullOrWhiteSpace(focusEntityKey))
            keys.Add(PersonaScenarioScope.SanitizeFolderSegment(focusEntityKey).ToLowerInvariant());

        record.Inference = new VisualAssetInference
        {
            Source = "vision",
            Confidence = infer.Confidence,
            EntityKeys = keys,
            Rationale = infer.Rationale
        };

        if (record.Subjects.Count == 0 && keys.Count > 0)
        {
            record.Subjects = keys
                .Select((k, i) => new VisualAssetSubject
                {
                    EntityKey = k,
                    Role = i == 0 ? "primary" : "secondary"
                })
                .ToList();
        }
    }
}
