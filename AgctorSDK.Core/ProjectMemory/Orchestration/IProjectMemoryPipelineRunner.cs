using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.ProjectMemory.Orchestration;

/// <summary>
/// Chains person-extractor → <see cref="Processing.IMemoryIntentProcessor"/> → projection → optional person-query.
/// In-process (no actor mailbox) for predictable ordering; actors remain available for interactive use.
/// </summary>
public interface IProjectMemoryPipelineRunner
{
    Task<ProjectMemoryPipelineResult> RunAsync(ProjectMemoryPipelineRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Parses <paramref name="rawExtractorLlmText"/> as <c>memoryIntents</c> JSON and applies routing/projection under the scenario workspace (same writes as pipeline ingest).
    /// </summary>
    Task<ProjectMemoryIngestResult> IngestFromExtractorOutputAsync(
        string projectRoot,
        string? scenarioId,
        string rawExtractorLlmText,
        CancellationToken cancellationToken = default);
}
