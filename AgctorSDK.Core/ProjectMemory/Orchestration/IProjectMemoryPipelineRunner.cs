using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.OutOfSchema;

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

    /// <summary>Appends user-approved unrouted facts to <c>.agctor/runtime/generic-inbox/confirmed.yaml</c> (PRD-019).</summary>
    Task<GenericInboxPersistResult> PersistApprovedGenericFactsAsync(
        string projectRoot,
        string? scenarioId,
        IReadOnlyList<ApprovedGenericFact> approvals,
        CancellationToken cancellationToken = default);
}
