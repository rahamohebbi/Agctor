using System.Collections.Generic;
using System.Linq;
using AgctorSDK.Core.ProjectMemory.Resolution.Models;

namespace AgctorSDK.Core.ProjectMemory.Resolution.Signals;

/// <summary>
/// Reduces a list of signals to a single confidence in [0, 1]. Positive signals are weighted-summed;
/// any negative signal caps the result below the hard threshold (PRD-018 §5.3 hard-veto rule).
/// </summary>
public static class ConfidenceCalculator
{
    public static double Compute(IEnumerable<ResolutionSignal> signals, ResolutionPolicy policy)
    {
        if (signals == null) return 0.0;

        double sum = 0.0;
        bool hasNegative = false;
        foreach (var s in signals)
        {
            if (s.IsNegative)
            {
                hasNegative = true;
                continue;
            }
            sum += s.Score * s.Weight;
        }

        if (sum < 0) sum = 0;
        if (sum > 1) sum = 1;

        if (hasNegative && sum >= policy.HardThreshold)
            sum = System.Math.Min(sum, policy.HardThreshold - 0.01); // cap just under hard

        return sum;
    }

    /// <summary>
    /// Count distinct signal families contributing positive scores (PRD-018 §5.6 P1). A "family" is
    /// the signal kind; this lets auto-promotion require two *different* sources of evidence.
    /// </summary>
    public static int IndependentPositiveFamilies(IEnumerable<ResolutionSignal> signals) =>
        signals?.Where(s => !s.IsNegative && s.Score > 0).Select(s => s.Kind).Distinct().Count() ?? 0;
}
