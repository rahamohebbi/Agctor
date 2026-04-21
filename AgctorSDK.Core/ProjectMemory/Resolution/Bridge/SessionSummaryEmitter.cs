using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.ProjectMemory.Resolution.Actors;
using AgctorSDK.Core.ProjectMemory.Resolution.Messages;
using AgctorSDK.Core.ProjectMemory.Resolution.Models;
using AgctorSDK.Core.ProjectMemory.Resolution.Persistence;

namespace AgctorSDK.Core.ProjectMemory.Resolution.Bridge;

/// <summary>
/// Emits a <see cref="SessionSummary"/> to the reconciler when a session closes or checkpoints.
/// Also persists the summary to <c>sessions/&lt;sessionId&gt;/summary.yaml</c> so the cross-session
/// flow survives restarts.
/// </summary>
/// <remarks>
/// Hosts call <see cref="EmitAsync"/> from their session-close hook. The emitter is intentionally
/// transport-free — no SSE, no HTTP — so CLIs and the dashboard use the same path.
/// </remarks>
public sealed class SessionSummaryEmitter
{
    private readonly IActorRuntimeAdapter _runtime;
    private readonly IResolutionActorAddressing _addressing;

    public SessionSummaryEmitter(IActorRuntimeAdapter runtime, IResolutionActorAddressing? addressing = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _addressing = addressing ?? new DefaultResolutionAddressing();
    }

    public async Task EmitAsync(
        string projectId,
        string projectRoot,
        string sessionId,
        IReadOnlyList<MentionRef> mentions,
        IReadOnlyList<string>? assertedFacts = null,
        IReadOnlyList<string>? negativeAssertions = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectId)) throw new ArgumentNullException(nameof(projectId));
        if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentNullException(nameof(sessionId));
        if (mentions == null) mentions = Array.Empty<MentionRef>();

        var summary = new SessionSummary
        {
            SessionId = sessionId,
            ProjectId = projectId,
            Mentions = new List<MentionRef>(mentions),
            AssertedFacts = assertedFacts == null ? new List<string>() : new List<string>(assertedFacts),
            NegativeAssertions = negativeAssertions == null ? new List<string>() : new List<string>(negativeAssertions)
        };

        if (!string.IsNullOrWhiteSpace(projectRoot))
        {
            try
            {
                new SessionSummaryStore(projectRoot).Save(summary);
            }
            catch
            {
                // Persistence is best-effort: the reconciler still gets the in-memory summary below.
            }
        }

        var recId = _addressing.ReconcilerIdFor(projectId);
        await _runtime.SendMessageAsync(recId, summary, senderId: "session-summary-emitter", cancellationToken: ct).ConfigureAwait(false);
    }
}
