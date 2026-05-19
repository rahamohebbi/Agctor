using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.OutOfSchema;

namespace AgctorSDK.Core.ProjectMemory.Inbox;

/// <summary>PRD-022a: UI/API layer for reviewing pending generic-inbox rows.</summary>
public interface IGenericInboxDecisionService
{
    Task<IReadOnlyList<GenericInboxPendingRow>> ListPendingAsync(
        string projectRoot,
        string? scenarioId,
        CancellationToken cancellationToken = default);

    Task<GenericInboxDecisionResult> ApplyDecisionsAsync(
        string projectRoot,
        string? scenarioId,
        IReadOnlyList<GenericInboxDecision> decisions,
        CancellationToken cancellationToken = default);
}

/// <summary>One approve/reject action from the confirmation inbox UI.</summary>
public sealed class GenericInboxDecision
{
    public string ProposalId { get; init; } = "";
    public bool Approve { get; init; }
}

/// <summary>Outcome of a batch inbox decision.</summary>
public sealed class GenericInboxDecisionResult
{
    public int Approved { get; init; }
    public int Rejected { get; init; }
    public int RejectedMismatch { get; init; }
    public IReadOnlyList<string> UpdatedFiles { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
}
