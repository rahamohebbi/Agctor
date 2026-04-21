using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Resolution.Actors;
using AgctorSDK.Core.ProjectMemory.Resolution.Messages;
using AgctorSDK.Core.ProjectMemory.Resolution.Models;

namespace AgctorSDK.Core.ProjectMemory.Resolution.Bridge;

/// <summary>
/// Publishes <see cref="MentionObserved"/> messages to the reconciler for each mention inferred
/// from extractor output. Kept as a thin, stateless bridge so ingest does not have to understand
/// resolution internals — it just hands the runner a batch of intents plus session metadata.
/// </summary>
/// <remarks>
/// Two input shapes are supported:
///   - <see cref="MemoryIntent"/> rows (one mention per intent whose attribute looks like a name).
///   - Raw relationship/narrative strings (each name token becomes a mention).
/// Both funnel through <see cref="PublishAsync"/> to keep coalescing consistent at the reconciler.
/// </remarks>
public sealed class MentionObservationPublisher
{
    private static readonly Regex NameToken = new(@"[A-Z][a-z]+(?:\s+[A-Z][a-z]+)?", RegexOptions.Compiled);

    private readonly IActorRuntimeAdapter _runtime;
    private readonly IResolutionActorAddressing _addressing;
    private readonly SessionMentionAccumulator? _accumulator;

    public MentionObservationPublisher(
        IActorRuntimeAdapter runtime,
        IResolutionActorAddressing? addressing = null,
        SessionMentionAccumulator? accumulator = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _addressing = addressing ?? new DefaultResolutionAddressing();
        _accumulator = accumulator;
    }

    /// <summary>Send every mention in <paramref name="mentions"/> to the project reconciler mailbox.</summary>
    public async Task PublishAsync(
        string projectId,
        IEnumerable<MentionRef> mentions,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectId)) throw new ArgumentNullException(nameof(projectId));
        if (mentions == null) return;
        var recId = _addressing.ReconcilerIdFor(projectId);
        foreach (var m in mentions)
        {
            if (m == null || string.IsNullOrWhiteSpace(m.SurfaceForm)) continue;
            if (!string.IsNullOrWhiteSpace(m.SessionId))
                _accumulator?.Record(m.SessionId!, m);
            await _runtime.SendMessageAsync(recId, new MentionObserved { Mention = m }, senderId: "mention-publisher", cancellationToken: ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Build <see cref="MentionRef"/>s from a routed memory-intent batch. Considers every intent
    /// whose <c>Value</c> contains a capitalized name token as a candidate mention and anchors it
    /// to the host entity (the intent's <c>EntityKey</c>) + field (the intent's <c>Attribute</c>).
    /// </summary>
    public static IReadOnlyList<MentionRef> FromMemoryIntents(
        IReadOnlyList<MemoryIntent> intents,
        string? scenarioId,
        string? sessionId,
        string? turnId)
    {
        var list = new List<MentionRef>();
        if (intents == null) return list;
        var scope = string.IsNullOrWhiteSpace(scenarioId) ? ResolutionScope.Project() : ResolutionScope.Scenario(scenarioId);

        foreach (var intent in intents)
        {
            if (intent == null || string.IsNullOrWhiteSpace(intent.Value)) continue;
            // Skip rows that are obviously not relationship references (dates, long sentences, …).
            foreach (Match m in NameToken.Matches(intent.Value))
            {
                var surface = m.Value.Trim();
                if (surface.Length < 2) continue;
                var mentionId = string.IsNullOrWhiteSpace(intent.Attribute)
                    ? $"{scope.ToKey()}:{intent.EntityKey}#{intent.KnowledgeType}.{surface}"
                    : $"{scope.ToKey()}:{intent.EntityKey}#{intent.Attribute}.{surface}";
                list.Add(new MentionRef
                {
                    MentionId = mentionId,
                    Scope = scope,
                    SurfaceForm = surface,
                    WithinEntityKey = intent.EntityKey,
                    Field = intent.Attribute,
                    SessionId = sessionId,
                    TurnId = turnId
                });
            }
        }
        return list;
    }
}
