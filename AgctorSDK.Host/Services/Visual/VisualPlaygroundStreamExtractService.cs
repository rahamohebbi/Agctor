using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Ollama;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Inbox;
using AgctorSDK.Core.ProjectMemory.Scenarios;
using AgctorSDK.Core.ProjectMemory.Scenarios.Messages;
using AgctorSDK.Core.ProjectMemory.Visual;
using AgctorSDK.Core.ProjectMemory.Visual.Actors;
using AgctorSDK.Host.Services.Scenarios;
using AgctorSDK.Core.ProjectMemory.Visual.Models;
using Microsoft.Extensions.Logging;

namespace AgctorSDK.Host.Services.Visual;

/// <summary>
/// Runs Gemma 4 infer+extract on the open playground SSE stream (PRD-023d) and emits
/// <c>visual_extract_*</c> / <c>attachment_state</c> events before the final <c>done</c>.
/// </summary>
public sealed class VisualPlaygroundStreamExtractService
{
    private readonly IVisualPipelineService _pipeline;
    private readonly IGenericInboxDecisionService _inbox;
    private readonly IScenarioFlowRuntimeStore? _flowStore;
    private readonly IScenarioFlowDomainEventPublisher? _flowDomainEvents;
    private readonly IScenarioFlowRuntimeOrchestrator? _flowOrchestrator;
    private readonly IScenarioCatalog? _scenarioCatalog;
    private readonly ILogger<VisualPlaygroundStreamExtractService> _logger;

    public VisualPlaygroundStreamExtractService(
        IVisualPipelineService pipeline,
        IGenericInboxDecisionService inbox,
        ILogger<VisualPlaygroundStreamExtractService> logger,
        IScenarioFlowDomainEventPublisher? flowDomainEvents = null,
        IScenarioFlowRuntimeStore? flowStore = null,
        IScenarioFlowRuntimeOrchestrator? flowOrchestrator = null,
        IScenarioCatalog? scenarioCatalog = null)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _flowDomainEvents = flowDomainEvents;
        _flowStore = flowStore;
        _flowOrchestrator = flowOrchestrator;
        _scenarioCatalog = scenarioCatalog;
    }

    public async Task<ScenarioFlowRuntimeResult?> RunAsync(
        string projectRoot,
        string scenarioId,
        string sessionId,
        IReadOnlyList<string> assetIds,
        string? userMessage,
        string? focusEntityKey,
        Func<string, string, Task> writeEventAsync,
        CancellationToken cancellationToken = default)
    {
        if (assetIds == null || assetIds.Count == 0 || string.IsNullOrWhiteSpace(scenarioId))
            return null;

        var modelHint = OllamaRuntimeConfiguration.GetVisionModelCandidates().FirstOrDefault() ?? "vision";
        var anyExtractSuccess = false;

        foreach (var rawId in assetIds)
        {
            if (string.IsNullOrWhiteSpace(rawId))
                continue;

            var assetId = rawId.Trim();
            cancellationToken.ThrowIfCancellationRequested();

            await writeEventAsync(
                    "visual_extract_started",
                    VisualPlaygroundAttachmentService.SerializeSsePayload(new { assetId, model = modelHint }))
                .ConfigureAwait(false);

            await writeEventAsync(
                    "attachment_state",
                    VisualPlaygroundAttachmentService.SerializeSsePayload(new
                    {
                        assetId,
                        state = VisualAssetStates.Extracting,
                        detail = "Analyzing photo…"
                    }))
                .ConfigureAwait(false);

            try
            {
                var infer = await _pipeline
                    .InferFromPromptAsync(
                        new VisualInferRequest
                        {
                            ProjectRoot = projectRoot,
                            ScenarioId = scenarioId,
                            AssetId = assetId,
                            UserMessage = userMessage,
                            FocusEntityKey = focusEntityKey
                        },
                        cancellationToken)
                    .ConfigureAwait(false);

                if (!infer.Success && !infer.Skipped)
                {
                    await EmitFailureAsync(writeEventAsync, assetId, infer.Error ?? "Vision infer failed.")
                        .ConfigureAwait(false);
                    continue;
                }

                var extract = await _pipeline
                    .ExtractAsync(
                        new VisualExtractRequest
                        {
                            ProjectRoot = projectRoot,
                            ScenarioId = scenarioId,
                            AssetId = assetId,
                            UserMessage = userMessage,
                            FocusEntityKey = focusEntityKey
                        },
                        cancellationToken)
                    .ConfigureAwait(false);

                if (!extract.Success && !extract.Skipped)
                {
                    await EmitFailureAsync(writeEventAsync, assetId, extract.Error ?? "Vision extract failed.")
                        .ConfigureAwait(false);
                    continue;
                }

                anyExtractSuccess = true;
                var detail = BuildSuccessDetail(extract);
                var inferConfidence = infer.Record?.Inference?.Confidence;
                var inferredSummary = BuildInferredSummary(infer.Record);
                await writeEventAsync(
                        "attachment_state",
                        VisualPlaygroundAttachmentService.SerializeSsePayload(new
                        {
                            assetId,
                            state = extract.Record?.State ?? VisualAssetStates.Ready,
                            detail
                        }))
                    .ConfigureAwait(false);

                await writeEventAsync(
                        "visual_extract_done",
                        VisualPlaygroundAttachmentService.SerializeSsePayload(new
                        {
                            assetId,
                            proposalCount = extract.ProposalCount,
                            model = extract.ModelUsed ?? modelHint,
                            skipped = extract.Skipped,
                            inferenceConfidence = inferConfidence,
                            inferredSummary
                        }))
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Playground stream visual extract failed for {AssetId}", assetId);
                await EmitFailureAsync(writeEventAsync, assetId, "Vision analysis failed.")
                    .ConfigureAwait(false);
            }
        }

        var pending = await _inbox
            .ListPendingAsync(projectRoot, scenarioId, cancellationToken)
            .ConfigureAwait(false);
        await writeEventAsync(
                "visual_inbox",
                VisualPlaygroundAttachmentService.SerializeSsePayload(new
                {
                    count = pending.Count,
                    scenarioId = PersonaScenarioScope.SanitizeFolderSegment(scenarioId)
                }))
            .ConfigureAwait(false);

        ScenarioFlowRuntimeResult? resumeResult = null;
        if (_flowDomainEvents != null && !string.IsNullOrWhiteSpace(sessionId))
        {
            var waitingForVisualExtract = await IsWaitingForVisualExtractAsync(
                    projectRoot,
                    sessionId,
                    scenarioId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (anyExtractSuccess || waitingForVisualExtract)
            {
                await writeEventAsync(
                        "flow_step",
                        VisualPlaygroundAttachmentService.SerializeSsePayload(new
                        {
                            id = "pm-style-refresh",
                            status = "running",
                            detail = "Updating style advice from your photos…"
                        }))
                    .ConfigureAwait(false);

                // Post-extract resume runs style LLM; don't abort when the HTTP stream client disconnects.
                using var resumeCts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
                var resumeCt = resumeCts.Token;
                try
                {
                    var extractedCount = assetIds.Count(id => !string.IsNullOrWhiteSpace(id));
                    var payload = new Dictionary<string, object?>
                    {
                        [ScenarioFlowDomainEventTypes.VisualExtractCompleted] = true,
                        ["visual.hasPhotos"] = true,
                        ["visual.extractedAssetCount"] = extractedCount
                    };
                    resumeResult = await _flowDomainEvents
                        .TryResumeAsync(
                            projectRoot,
                            sessionId,
                            scenarioId,
                            ScenarioFlowDomainEventTypes.VisualExtractCompleted,
                            payload,
                            resumeCt)
                        .ConfigureAwait(false);

                    if (resumeResult == null && waitingForVisualExtract)
                    {
                        _logger.LogWarning(
                            "Scenario flow still waiting for {EventType} after visual extract for session {SessionId}.",
                            ScenarioFlowDomainEventTypes.VisualExtractCompleted,
                            sessionId);
                    }

                    if (resumeResult == null)
                    {
                        resumeResult = await TryAdvanceStuckPhotoCollectionAsync(
                                projectRoot,
                                scenarioId,
                                sessionId,
                                resumeCt)
                            .ConfigureAwait(false);
                    }

                    // Recovery may advance n_ask → n_await; publish extract completed and continue the segment.
                    if (resumeResult != null
                        && !resumeResult.Completed
                        && resumeResult.Status == ScenarioFlowRuntimeStatus.WaitingForDomainEvent)
                    {
                        resumeResult = await _flowDomainEvents
                                .TryResumeAsync(
                                    projectRoot,
                                    sessionId,
                                    scenarioId,
                                    ScenarioFlowDomainEventTypes.VisualExtractCompleted,
                                    payload,
                                    resumeCt)
                            .ConfigureAwait(false)
                            ?? resumeResult;
                    }

                    var refreshDetail = resumeResult?.Success == true && resumeResult.Completed
                        ? "Style advice updated from your photos."
                        : resumeResult?.Success == false
                            ? resumeResult.ErrorMessage ?? "Style refresh failed."
                            : "Style refresh pending.";
                    await writeEventAsync(
                            "flow_step",
                            VisualPlaygroundAttachmentService.SerializeSsePayload(new
                            {
                                id = "pm-style-refresh",
                                status = resumeResult?.Success == true ? "done" : "error",
                                detail = refreshDetail
                            }))
                        .ConfigureAwait(false);
                }
                catch (Exception exResume)
                {
                    _logger.LogWarning(exResume, "Scenario flow resume after visual extract failed.");
                }
            }
        }

        return resumeResult;
    }

    private async Task<ScenarioFlowRuntimeResult?> TryAdvanceStuckPhotoCollectionAsync(
        string projectRoot,
        string sessionId,
        string scenarioId,
        CancellationToken cancellationToken)
    {
        if (_flowOrchestrator == null || _scenarioCatalog == null)
            return null;

        var def = _scenarioCatalog.Get(scenarioId.Trim());
        if (def?.Flow == null)
            return null;

        try
        {
            return await _flowOrchestrator
                .TryAdvanceStuckPhotoCollectionAsync(
                    scenarioId.Trim(),
                    def,
                    projectRoot,
                    sessionId,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Scenario flow stuck photo recovery failed for session {SessionId}.", sessionId);
            return null;
        }
    }

    private async Task<bool> IsWaitingForVisualExtractAsync(
        string projectRoot,
        string sessionId,
        string scenarioId,
        CancellationToken cancellationToken)
    {
        if (_flowStore == null)
            return false;

        var snapshot = await _flowStore
            .LoadAsync(projectRoot, sessionId, scenarioId, cancellationToken)
            .ConfigureAwait(false);
        return snapshot?.Status == ScenarioFlowRuntimeStatus.WaitingForDomainEvent
               && string.Equals(
                   snapshot.AwaitingEvent?.EventType,
                   ScenarioFlowDomainEventTypes.VisualExtractCompleted,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildSuccessDetail(VisualExtractResult extract)
    {
        if (extract.Skipped)
            return "Analysis skipped.";

        if (extract.ProposalCount > 0)
            return $"Insights ready · {extract.ProposalCount} memories to review";

        return "Photo analyzed.";
    }

    private static string? BuildInferredSummary(VisualAssetRecord? record)
    {
        if (record?.Inference?.EntityKeys == null || record.Inference.EntityKeys.Count == 0)
            return null;

        var who = string.Join(
            " · ",
            record.Inference.EntityKeys
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k =>
                {
                    var t = k.Trim();
                    return t.Length == 1
                        ? t.ToUpperInvariant()
                        : char.ToUpperInvariant(t[0]) + t[1..];
                }));

        if (string.IsNullOrWhiteSpace(who))
            return null;

        return $"Understood: {who}";
    }

    private static async Task EmitFailureAsync(
        Func<string, string, Task> writeEventAsync,
        string assetId,
        string detail)
    {
        await writeEventAsync(
                "attachment_state",
                VisualPlaygroundAttachmentService.SerializeSsePayload(new
                {
                    assetId,
                    state = VisualAssetStates.Failed,
                    detail
                }))
            .ConfigureAwait(false);

        await writeEventAsync(
                "visual_extract_done",
                VisualPlaygroundAttachmentService.SerializeSsePayload(new
                {
                    assetId,
                    proposalCount = 0,
                    error = detail
                }))
            .ConfigureAwait(false);
    }
}
