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

    /// <summary>Reads confirmed rows (used by the back-fill replay service to project rows into entity files).</summary>
    Task<IReadOnlyList<GenericInboxConfirmedRow>> LoadConfirmedAsync(
        string projectRoot,
        CancellationToken cancellationToken = default);

    /// <summary>Stamps <see cref="GenericInboxConfirmedRow.ReplayedAtUtc"/> on rows that were successfully back-projected.</summary>
    Task<int> MarkReplayedAsync(
        string projectRoot,
        IReadOnlyList<string> proposalIds,
        string replayedAtUtc,
        CancellationToken cancellationToken = default);
}
