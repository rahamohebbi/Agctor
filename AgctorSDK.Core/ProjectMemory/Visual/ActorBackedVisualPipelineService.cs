using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Visual.Actors;
using Microsoft.Extensions.Logging;

namespace AgctorSDK.Core.ProjectMemory.Visual;

/// <summary>Facade over <see cref="VisualPipelineService"/> with background playground queue.</summary>
public sealed class ActorBackedVisualPipelineService : IVisualPipelineService
{
    private readonly VisualPipelineService _pipeline;
    private readonly ILogger<ActorBackedVisualPipelineService> _logger;

    public ActorBackedVisualPipelineService(
        VisualPipelineService pipeline,
        ILogger<ActorBackedVisualPipelineService> logger)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _logger = logger;
    }

    public Task<VisualIngestEnrichResult> EnrichIngestAsync(
        VisualIngestEnrichRequest request,
        CancellationToken cancellationToken = default) =>
        _pipeline.EnrichIngestAsync(request, cancellationToken);

    public Task<VisualInferResult> InferFromPromptAsync(
        VisualInferRequest request,
        CancellationToken cancellationToken = default) =>
        _pipeline.InferFromPromptAsync(request, cancellationToken);

    public Task<VisualExtractResult> ExtractAsync(
        VisualExtractRequest request,
        CancellationToken cancellationToken = default) =>
        _pipeline.ExtractAsync(request, cancellationToken);

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
                    var infer = await _pipeline.InferFromPromptAsync(new VisualInferRequest
                    {
                        ProjectRoot = projectRoot,
                        ScenarioId = scenarioId,
                        AssetId = assetId.Trim(),
                        UserMessage = userMessage,
                        FocusEntityKey = focusEntityKey
                    }, CancellationToken.None).ConfigureAwait(false);

                    if (!infer.Success)
                    {
                        _logger.LogWarning(
                            "Visual infer failed for asset {AssetId}: {Error}",
                            assetId,
                            infer.Error);
                    }

                    var extract = await _pipeline.ExtractAsync(new VisualExtractRequest
                    {
                        ProjectRoot = projectRoot,
                        ScenarioId = scenarioId,
                        AssetId = assetId.Trim(),
                        UserMessage = userMessage,
                        FocusEntityKey = focusEntityKey
                    }, CancellationToken.None).ConfigureAwait(false);

                    if (!extract.Success && !extract.Skipped)
                    {
                        _logger.LogWarning(
                            "Visual extract failed for asset {AssetId}: {Error}",
                            assetId,
                            extract.Error);
                    }
                    else if (extract.ProposalCount > 0)
                    {
                        _logger.LogInformation(
                            "Visual extract queued {Count} inbox proposal(s) for asset {AssetId}",
                            extract.ProposalCount,
                            assetId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Background visual pipeline failed for asset {AssetId}", assetId);
                }
            }
        });
    }
}
