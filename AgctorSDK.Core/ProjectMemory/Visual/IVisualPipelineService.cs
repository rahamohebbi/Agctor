using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Visual.Actors;

namespace AgctorSDK.Core.ProjectMemory.Visual;

/// <summary>Actor-backed visual infer/extract/ingest enrichment (PRD-023d).</summary>
public interface IVisualPipelineService
{
    Task<VisualIngestEnrichResult> EnrichIngestAsync(
        VisualIngestEnrichRequest request,
        CancellationToken cancellationToken = default);

    Task<VisualInferResult> InferFromPromptAsync(
        VisualInferRequest request,
        CancellationToken cancellationToken = default);

    Task<VisualExtractResult> ExtractAsync(
        VisualExtractRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Fire-and-forget extract for playground attachments (does not block the HTTP stream).</summary>
    void QueueExtractForAssets(
        string projectRoot,
        string scenarioId,
        IReadOnlyList<string> assetIds,
        string? userMessage,
        string? focusEntityKey);
}
