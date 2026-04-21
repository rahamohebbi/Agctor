using System.Collections.Generic;

namespace AgctorSDK.Core.ProjectMemory.Resolution.Models;

/// <summary>
/// Project-scoped configuration loaded from <c>.agctor/resolution.yaml</c>.
/// Defaults are conservative so the subsystem is safe to enable incrementally (PRD-018 §5.8).
/// </summary>
public sealed class ResolutionPolicy
{
    public bool Enabled { get; set; } = false;
    public double HardThreshold { get; set; } = 0.90;
    public double SoftThreshold { get; set; } = 0.55;

    /// <summary>
    /// Per-signal-kind weights. Missing kinds default to 0 weight (ignored in confidence sum).
    /// </summary>
    public Dictionary<string, double> SignalWeights { get; set; } = new()
    {
        { "aliasMatch",      0.25 },
        { "uniqueness",      0.20 },
        { "corefInSession",  0.20 },
        { "attrOverlap",     0.15 },
        { "embedding",       0.10 },
        { "graphConsistency",0.10 }
    };

    /// <summary>
    /// Reconciler mailbox behavior.
    /// </summary>
    public ReconcilerOptions Reconciler { get; set; } = new();

    /// <summary>
    /// Promotion / review behavior.
    /// </summary>
    public ReviewOptions Review { get; set; } = new();

    public static ResolutionPolicy CreateDefault() => new();

    public double WeightFor(string signalKind)
    {
        if (SignalWeights == null) return 0;
        return SignalWeights.TryGetValue(signalKind, out var w) ? w : 0;
    }
}

public sealed class ReconcilerOptions
{
    public int CoalesceWindowMs { get; set; } = 2000;
    public int PerEntityQueueSize { get; set; } = 32;
    public int BatchSize { get; set; } = 16;
}

public sealed class ReviewOptions
{
    public bool AutoPromote { get; set; } = true;
    public bool RequireReviewer { get; set; } = false;

    /// <summary>
    /// Minimum number of independent signal families needed alongside a threshold crossing
    /// for auto-promotion (PRD-018 §5.6 P1).
    /// </summary>
    public int MinIndependentSignalFamiliesForAutoPromote { get; set; } = 2;
}
