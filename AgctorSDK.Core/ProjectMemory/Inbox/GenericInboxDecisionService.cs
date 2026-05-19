using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.OutOfSchema;

namespace AgctorSDK.Core.ProjectMemory.Inbox;

/// <summary>
/// Lists pending generic-inbox facts and applies approve/reject decisions using the
/// same persist + replay path as chat confirmation (PRD-019).
/// </summary>
public sealed class GenericInboxDecisionService : IGenericInboxDecisionService
{
    private readonly IGenericInboxStore _store;
    private readonly IGenericInboxReplayService? _replay;

    public GenericInboxDecisionService(IGenericInboxStore store, IGenericInboxReplayService? replay = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _replay = replay;
    }

    public async Task<IReadOnlyList<GenericInboxPendingRow>> ListPendingAsync(
        string projectRoot,
        string? scenarioId,
        CancellationToken cancellationToken = default)
    {
        var all = await _store.LoadPendingAsync(projectRoot, cancellationToken).ConfigureAwait(false);
        var seg = NormalizeScenarioSegment(scenarioId);
        if (string.IsNullOrEmpty(seg))
            return all;

        return all
            .Where(r => string.Equals(r.ScenarioSegment ?? "", seg, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public async Task<GenericInboxDecisionResult> ApplyDecisionsAsync(
        string projectRoot,
        string? scenarioId,
        IReadOnlyList<GenericInboxDecision> decisions,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        if (decisions == null || decisions.Count == 0)
        {
            return new GenericInboxDecisionResult { Errors = ["No decisions provided."] };
        }

        var seg = NormalizeScenarioSegment(scenarioId);
        var pending = await ListPendingAsync(projectRoot, scenarioId, cancellationToken).ConfigureAwait(false);
        var byId = pending.ToDictionary(r => r.ProposalId, StringComparer.OrdinalIgnoreCase);

        var toApprove = new List<ApprovedGenericFact>();
        var toReject = new List<string>();

        foreach (var d in decisions)
        {
            if (string.IsNullOrWhiteSpace(d.ProposalId))
            {
                errors.Add("Decision missing proposalId.");
                continue;
            }

            if (!byId.TryGetValue(d.ProposalId.Trim(), out var row))
            {
                errors.Add("Unknown or stale proposalId: " + d.ProposalId);
                continue;
            }

            if (d.Approve)
            {
                toApprove.Add(new ApprovedGenericFact
                {
                    ProposalId = row.ProposalId,
                    EntityKey = row.EntityKey,
                    KnowledgeType = row.KnowledgeType,
                    Attribute = row.Attribute,
                    Value = row.Value,
                    Confidence = row.Confidence
                });
            }
            else
            {
                toReject.Add(row.ProposalId);
            }
        }

        var rejected = 0;
        if (toReject.Count > 0)
        {
            rejected = await _store
                .DropPendingAsync(projectRoot, toReject, cancellationToken)
                .ConfigureAwait(false);
        }

        var approved = 0;
        var mismatch = 0;
        IReadOnlyList<string> updatedFiles = Array.Empty<string>();

        if (toApprove.Count > 0)
        {
            var persist = await _store
                .PersistApprovedAsync(projectRoot, string.IsNullOrEmpty(seg) ? null : seg, toApprove, cancellationToken)
                .ConfigureAwait(false);
            approved = persist.Appended;
            mismatch = persist.RejectedMismatch;
            if (persist.Errors.Count > 0)
                errors.AddRange(persist.Errors);

            if (_replay != null && persist.Appended > 0)
            {
                try
                {
                    var report = await _replay
                        .ReplayAsync(projectRoot, string.IsNullOrEmpty(seg) ? null : seg, null, cancellationToken)
                        .ConfigureAwait(false);
                    updatedFiles = report.UpdatedFiles;
                }
                catch (Exception ex)
                {
                    errors.Add("Replay failed: " + ex.Message);
                }
            }
        }

        return new GenericInboxDecisionResult
        {
            Approved = approved,
            Rejected = rejected,
            RejectedMismatch = mismatch,
            UpdatedFiles = updatedFiles,
            Errors = errors
        };
    }

    private static string NormalizeScenarioSegment(string? scenarioId) =>
        string.IsNullOrWhiteSpace(scenarioId) ? "" : PersonaScenarioScope.SanitizeFolderSegment(scenarioId);
}
