using AgctorSDK.Core.ProjectMemory.Resolution.Models;

namespace AgctorSDK.Core.ProjectMemory.Resolution.Signals;

/// <summary>
/// S6 graph consistency: would linking this mention introduce a contradiction in the mention graph?
/// v1 rule is conservative — it simply checks that the mention's host entity (if any) is not the
/// same as the candidate (an entity cannot be its own parent/child via a same-folder mention).
/// Returns a small positive score when the check passes, or a negative signal when it fails.
/// </summary>
/// <remarks>
/// A richer implementation can inspect existing hard edges for cycles, incompatible dates, or
/// duplicate role assignments. The contract is intentionally minimal so callers can swap in a
/// project-specific consistency rule without touching actor code.
/// </remarks>
public sealed class GraphConsistency : ISignalProducer
{
    public string Name => "GraphConsistency@1";
    public string Kind => "graphConsistency";

    public ResolutionSignal? Score(SignalContext ctx, ResolutionPolicy policy)
    {
        if (string.IsNullOrWhiteSpace(ctx.CandidateEntityKey))
            return null;

        var host = ctx.Mention?.WithinEntityKey;
        if (!string.IsNullOrWhiteSpace(host) &&
            string.Equals(host, ctx.CandidateEntityKey, System.StringComparison.OrdinalIgnoreCase))
        {
            return new ResolutionSignal
            {
                Kind = Kind,
                ProducedBy = Name,
                Score = 1.0,
                Weight = 1.0,
                IsNegative = true,
                Rationale = $"Self-reference: mention hosted in '{host}' cannot canonically resolve to the same entity",
                InputsFingerprint = FingerprintUtil.Of(Kind, "selfref", host!, ctx.CandidateEntityKey)
            };
        }

        // Passing the minimal check gives a small positive signal — absence of contradiction is
        // weak evidence on its own, but combined with alias+uniqueness it helps clear the hard bar.
        return new ResolutionSignal
        {
            Kind = Kind,
            ProducedBy = Name,
            Score = 0.5,
            Weight = policy.WeightFor(Kind),
            Rationale = "No graph contradiction detected (v1 check: no self-reference)",
            InputsFingerprint = FingerprintUtil.Of(Kind, "ok", host ?? "", ctx.CandidateEntityKey)
        };
    }
}
