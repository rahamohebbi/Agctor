using System;
using AgctorSDK.Core.ProjectMemory.Resolution.Models;

namespace AgctorSDK.Core.ProjectMemory.Resolution.Signals;

/// <summary>
/// S2 surface-form uniqueness: score = 1 / N where N is the number of entities whose display name
/// or alias set matches the (normalized) surface form. Boosts confidence when a name is rare in
/// the project; stays neutral-low when it collides.
/// </summary>
public sealed class SurfaceUniqueness : ISignalProducer
{
    public string Name => "SurfaceUniqueness@1";
    public string Kind => "uniqueness";

    public ResolutionSignal? Score(SignalContext ctx, ResolutionPolicy policy)
    {
        if (ctx.TotalEntitiesMatchingSurface <= 0) return null;

        double s = 1.0 / Math.Max(1, ctx.TotalEntitiesMatchingSurface);
        return new ResolutionSignal
        {
            Kind = Kind,
            ProducedBy = Name,
            Score = s,
            Weight = policy.WeightFor(Kind),
            Rationale = $"{ctx.TotalEntitiesMatchingSurface} entity(ies) match surface '{ctx.Mention.SurfaceForm}' (normalized)",
            InputsFingerprint = FingerprintUtil.Of(
                Kind,
                SurfaceNormalizer.Normalize(ctx.Mention.SurfaceForm),
                ctx.CandidateEntityKey,
                ctx.TotalEntitiesMatchingSurface.ToString())
        };
    }
}
