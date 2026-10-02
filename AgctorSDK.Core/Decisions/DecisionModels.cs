using System;
using System.Collections.Generic;

namespace AgctorSDK.Core.Decisions
{
    /// <summary>
    /// What an actor wants decided. The fabric chooses the engine; the actor does not.
    /// </summary>
    public class DecisionRequest
    {
        public string? Id { get; set; }
        public DecisionType Type { get; set; }
        public string? Question { get; set; }
        public object? State { get; set; }
        public List<DecisionOption>? Options { get; set; }
        public DecisionConstraints? Constraints { get; set; }
        public DecisionPolicy? Policy { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    /// <summary>
    /// One selectable option. <see cref="Id"/> is the value returned for a choice decision.
    /// </summary>
    public class DecisionOption
    {
        public DecisionOption()
        {
        }

        public DecisionOption(string id, string? description = null)
        {
            Id = id;
            Description = description ?? id;
        }

        public string Id { get; set; } = string.Empty;
        public string? Description { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    /// <summary>
    /// Numeric bounds and optional deterministic rules.
    /// Rules live here so a caller can express an exact policy without referencing a provider.
    /// </summary>
    public class DecisionConstraints
    {
        public double? Min { get; set; }
        public double? Max { get; set; }
        public List<DecisionRule>? Rules { get; set; }
    }

    /// <summary>
    /// A predicate the rule provider can evaluate against <see cref="DecisionRequest.State"/> or metadata.
    /// An empty <see cref="Field"/> matches unconditionally.
    /// </summary>
    public class DecisionRule
    {
        public string? Field { get; set; }
        public object? EqualsValue { get; set; }
        public string? Select { get; set; }
        public bool? Value { get; set; }
        public double? Score { get; set; }
        public double Confidence { get; set; } = 1.0;
    }

    /// <summary>
    /// How an actor influences routing without naming an engine in its call site.
    /// </summary>
    public class DecisionPolicy
    {
        public bool PreferLocal { get; set; }
        public bool PreferFast { get; set; }
        public bool PreferCheap { get; set; }
        public bool PreferAccurate { get; set; }
        public bool RequireLocal { get; set; }
        public decimal? MaxCost { get; set; }
        public TimeSpan? MaxLatency { get; set; }
        public int? MaxLatencyMs { get; set; }
        public double? MinimumConfidence { get; set; }
        public List<string>? AllowedProviders { get; set; }
        public List<string>? BlockedProviders { get; set; }

        /// <summary>
        /// Stricter of <see cref="MaxLatency"/> and <see cref="MaxLatencyMs"/> when both are set.
        /// </summary>
        public TimeSpan? LatencyBudget
        {
            get
            {
                TimeSpan? fromMs = MaxLatencyMs.HasValue ? TimeSpan.FromMilliseconds(MaxLatencyMs.Value) : null;
                if (MaxLatency.HasValue && fromMs.HasValue)
                {
                    return MaxLatency.Value <= fromMs.Value ? MaxLatency : fromMs;
                }

                return MaxLatency ?? fromMs;
            }
        }
    }

    /// <summary>
    /// Normalized outcome returned to the actor. No chain-of-thought; only an optional short summary.
    /// </summary>
    public class DecisionResult
    {
        public string? DecisionId { get; set; }
        public string? Provider { get; set; }
        public object? Value { get; set; }
        public double Confidence { get; set; }
        public Dictionary<string, double>? Scores { get; set; }
        public string? ReasoningSummary { get; set; }
        public TimeSpan Latency { get; set; }
        public decimal? Cost { get; set; }
        public bool Escalated { get; set; }

        /// <summary>
        /// Provider that answered before a low-confidence escalation. Null when <see cref="Escalated"/> is false.
        /// </summary>
        public string? PreviousProvider { get; set; }

        public double LatencyMs => Latency.TotalMilliseconds;
    }

    /// <summary>
    /// Facts the router needs besides the request itself: who asked, and which providers already failed.
    /// </summary>
    public class DecisionContext
    {
        public string? ActorId { get; set; }
        public string? ActorType { get; set; }
        public DecisionPolicy? Policy { get; set; }
        public List<string>? ExcludedProviders { get; set; }

        /// <summary>
        /// When true, the router must leave the current engine and pick a stronger one (the LLM).
        /// </summary>
        public bool Escalate { get; set; }
    }

    /// <summary>
    /// Mailbox message from an actor context to the decision actor.
    /// Carries the caller identity so routing policy can see who asked.
    /// </summary>
    public class ActorDecisionMessage
    {
        public string? ActorId { get; set; }
        public string? ActorType { get; set; }
        public DecisionRequest Request { get; set; } = new DecisionRequest();
    }
}
