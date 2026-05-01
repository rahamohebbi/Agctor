using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.ProjectMemory.OutOfSchema;

/// <summary>Project-level generic inbox under <c>.agctor/runtime/generic-inbox/</c> (PRD-019).</summary>
public interface IGenericInboxStore
{
    /// <summary>Appends any proposals the runtime surfaces (immediate or review band); skips ids already pending or confirmed.</summary>
    Task AppendPendingAsync(
        string projectRoot,
        string? scenarioSegment,
        IReadOnlyList<OutOfSchemaFactProposal> proposals,
        CancellationToken cancellationToken = default);

    /// <summary>Validates proposal hashes, appends to confirmed, removes matching pending rows.</summary>
    Task<GenericInboxPersistResult> PersistApprovedAsync(
        string projectRoot,
        string? scenarioSegment,
        IReadOnlyList<ApprovedGenericFact> approvals,
        CancellationToken cancellationToken = default);

    /// <summary>Reads pending rows (for confirmation flows and reviewers).</summary>
    Task<IReadOnlyList<GenericInboxPendingRow>> LoadPendingAsync(
        string projectRoot,
        CancellationToken cancellationToken = default);

    /// <summary>Removes pending rows matching <paramref name="proposalIds"/>. Returns how many rows were dropped.</summary>
    Task<int> DropPendingAsync(
        string projectRoot,
        IReadOnlyList<string> proposalIds,
        CancellationToken cancellationToken = default);
}
