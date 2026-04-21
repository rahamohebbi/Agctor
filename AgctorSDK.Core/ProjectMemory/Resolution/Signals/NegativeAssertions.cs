using System.Linq;
using AgctorSDK.Core.ProjectMemory.Resolution.Models;

namespace AgctorSDK.Core.ProjectMemory.Resolution.Signals;

/// <summary>
/// S7 negative evidence: checks session-level negative assertions for a phrase like
/// "different &lt;entity&gt;" or "not &lt;entity&gt;". Emits a negative signal that the actor's
/// confidence sum treats as a hard veto (cap below hard threshold). Intentionally conservative so
/// a single throwaway sentence does not tank a strong positive.
/// </summary>
public sealed class NegativeAssertions : ISignalProducer
{
    public string Name => "NegativeAssertions@1";
    public string Kind => "negative";

    public ResolutionSignal? Score(SignalContext ctx, ResolutionPolicy policy)
    {
        if (ctx.SessionNegativeAssertions == null || ctx.SessionNegativeAssertions.Count == 0)
            return null;

        var needle = SurfaceNormalizer.Normalize(ctx.CandidateDisplayName);
        if (string.IsNullOrEmpty(needle)) return null;

        var hit = ctx.SessionNegativeAssertions
            .Select(SurfaceNormalizer.Normalize)
            .FirstOrDefault(a => !string.IsNullOrEmpty(a) && a.Contains(needle));

        if (hit == null) return null;

        return new ResolutionSignal
        {
            Kind = Kind,
            ProducedBy = Name,
            Score = 1.0,
            Weight = 1.0,
            IsNegative = true,
            Rationale = $"Session contains negative assertion about '{ctx.CandidateDisplayName}': '{hit}'",
            InputsFingerprint = FingerprintUtil.Of(Kind, ctx.CandidateEntityKey, hit)
        };
    }
}
