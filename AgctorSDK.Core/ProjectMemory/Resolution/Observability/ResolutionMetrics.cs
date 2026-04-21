using System.Collections.Concurrent;

namespace AgctorSDK.Core.ProjectMemory.Resolution.Observability;

/// <summary>
/// Lightweight in-process counters for the resolution subsystem. Per-project keys keep dashboards
/// honest in multi-project hosts. Intentionally tiny: once the host wires a real metrics stack
/// (Prometheus, OTEL) these values can be re-exported without touching actors.
/// </summary>
public sealed class ResolutionMetrics
{
    private readonly ConcurrentDictionary<string, long> _counters = new();

    public long Increment(string key, long delta = 1)
    {
        return _counters.AddOrUpdate(key, delta, (_, v) => v + delta);
    }

    public long Get(string key) => _counters.TryGetValue(key, out var v) ? v : 0;

    public System.Collections.Generic.IReadOnlyDictionary<string, long> Snapshot()
    {
        var copy = new System.Collections.Generic.Dictionary<string, long>();
        foreach (var kv in _counters) copy[kv.Key] = kv.Value;
        return copy;
    }

    public static class Keys
    {
        public static string MentionsObserved(string projectId) => $"resolution.{projectId}.mentions.observed";
        public static string CandidatesDispatched(string projectId) => $"resolution.{projectId}.candidates.dispatched";
        public static string CandidatesCoalesced(string projectId) => $"resolution.{projectId}.candidates.coalesced";
        public static string EdgesCreated(string projectId) => $"resolution.{projectId}.edges.created";
        public static string EdgesUpdated(string projectId) => $"resolution.{projectId}.edges.updated";
        public static string AutoPromotions(string projectId) => $"resolution.{projectId}.promotions.auto";
        public static string OperatorPromotions(string projectId) => $"resolution.{projectId}.promotions.user";
        public static string Demotions(string projectId) => $"resolution.{projectId}.demotions";
        public static string Rejections(string projectId) => $"resolution.{projectId}.rejections";
        public static string IntentsEmitted(string projectId) => $"resolution.{projectId}.intents.emitted";
    }
}
