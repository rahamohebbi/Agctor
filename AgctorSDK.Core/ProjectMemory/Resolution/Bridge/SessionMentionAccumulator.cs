using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using AgctorSDK.Core.ProjectMemory.Resolution.Models;

namespace AgctorSDK.Core.ProjectMemory.Resolution.Bridge;

/// <summary>
/// Process-wide store of mentions observed during a session, indexed by <c>sessionId</c>. The
/// <see cref="SessionSummaryEmitter"/> consults it when a session closes so the summary carries the
/// facts/mentions the reconciler needs for cross-session linking (PRD-018 §5.3, §6 criterion 2).
/// </summary>
/// <remarks>
/// Kept in-memory on purpose: summaries are idempotent, and crashing the Host simply means the
/// next session's summary picks up where this one left off. Bounded per session to keep memory
/// predictable under chatty sessions.
/// </remarks>
public sealed class SessionMentionAccumulator
{
    private const int MaxMentionsPerSession = 1024;
    private readonly ConcurrentDictionary<string, List<MentionRef>> _bySession = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, List<string>> _factsBySession = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, List<string>> _negativesBySession = new(StringComparer.Ordinal);

    public void Record(string sessionId, MentionRef mention)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || mention == null || string.IsNullOrWhiteSpace(mention.SurfaceForm))
            return;
        var list = _bySession.GetOrAdd(sessionId, _ => new List<MentionRef>());
        lock (list)
        {
            if (list.Count >= MaxMentionsPerSession) return;
            list.Add(mention);
        }
    }

    public void RecordFact(string sessionId, string fact)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(fact)) return;
        var list = _factsBySession.GetOrAdd(sessionId, _ => new List<string>());
        lock (list) list.Add(fact);
    }

    public void RecordNegative(string sessionId, string neg)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(neg)) return;
        var list = _negativesBySession.GetOrAdd(sessionId, _ => new List<string>());
        lock (list) list.Add(neg);
    }

    public IReadOnlyList<MentionRef> Snapshot(string sessionId)
    {
        if (!_bySession.TryGetValue(sessionId, out var list)) return Array.Empty<MentionRef>();
        lock (list) return list.ToList();
    }

    public IReadOnlyList<string> Facts(string sessionId) =>
        _factsBySession.TryGetValue(sessionId, out var l) ? l.ToList() : (IReadOnlyList<string>)Array.Empty<string>();

    public IReadOnlyList<string> Negatives(string sessionId) =>
        _negativesBySession.TryGetValue(sessionId, out var l) ? l.ToList() : (IReadOnlyList<string>)Array.Empty<string>();

    public void Clear(string sessionId)
    {
        _bySession.TryRemove(sessionId, out _);
        _factsBySession.TryRemove(sessionId, out _);
        _negativesBySession.TryRemove(sessionId, out _);
    }
}
