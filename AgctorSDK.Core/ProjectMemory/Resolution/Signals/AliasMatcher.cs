using System.Linq;
using AgctorSDK.Core.ProjectMemory.Resolution.Models;

namespace AgctorSDK.Core.ProjectMemory.Resolution.Signals;

/// <summary>
/// S1 alias/display-name match: score 1.0 on exact (normalized) match, 0.6 on substring containment
/// in either direction, 0 otherwise. Kept deliberately simple — richer lexical matching (Jaro,
/// tokenized) can replace this without touching the contract.
/// </summary>
public sealed class AliasMatcher : ISignalProducer
{
    public string Name => "AliasMatcher@1";
    public string Kind => "aliasMatch";

    public ResolutionSignal? Score(SignalContext ctx, ResolutionPolicy policy)
    {
        var surface = SurfaceNormalizer.Normalize(ctx.Mention.SurfaceForm);
        if (string.IsNullOrEmpty(surface))
            return null;

        var candidates = new[] { ctx.CandidateDisplayName }
            .Concat(ctx.CandidateAliases ?? System.Array.Empty<string>())
            .Select(SurfaceNormalizer.Normalize)
            .Where(x => !string.IsNullOrEmpty(x))
            .ToArray();

        if (candidates.Length == 0) return null;

        double best = 0.0;
        string matched = "";
        foreach (var c in candidates)
        {
            double score;
            if (c == surface) score = 1.0;
            else if (c.Contains(surface) || surface.Contains(c)) score = 0.6;
            else score = 0.0;

            if (score > best)
            {
                best = score;
                matched = c;
            }
        }

        if (best <= 0) return null;

        return new ResolutionSignal
        {
            Kind = Kind,
            ProducedBy = Name,
            Score = best,
            Weight = policy.WeightFor(Kind),
            Rationale = best >= 1.0
                ? $"'{ctx.Mention.SurfaceForm}' matches '{matched}' exactly (normalized)"
                : $"'{ctx.Mention.SurfaceForm}' substring-matches '{matched}' (normalized)",
            InputsFingerprint = FingerprintUtil.Of(Kind, surface, ctx.CandidateEntityKey, matched)
        };
    }
}
