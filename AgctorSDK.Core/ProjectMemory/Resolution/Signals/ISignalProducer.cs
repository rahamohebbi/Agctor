using System.Collections.Generic;
using AgctorSDK.Core.ProjectMemory.Resolution.Models;

namespace AgctorSDK.Core.ProjectMemory.Resolution.Signals;

/// <summary>
/// Context passed to a signal producer when scoring a candidate mention -> entity edge.
/// Kept lightweight on purpose; producers that need more (e.g. embeddings) should receive their
/// dependencies via constructor injection, not through this context.
/// </summary>
public sealed class SignalContext
{
    public MentionRef Mention { get; set; } = new();
    public string CandidateEntityKey { get; set; } = "";
    public string CandidateEntityPath { get; set; } = "";
    public string CandidateDisplayName { get; set; } = "";
    public IReadOnlyList<string> CandidateAliases { get; set; } = System.Array.Empty<string>();
    public int TotalEntitiesMatchingSurface { get; set; } = 1;
    public IReadOnlyList<string> SessionAssertedFacts { get; set; } = System.Array.Empty<string>();
    public IReadOnlyList<string> SessionNegativeAssertions { get; set; } = System.Array.Empty<string>();

    /// <summary>
    /// Other entities already linked to this candidate (via existing hard or soft incoming edges).
    /// Allows graph-consistency signals to reason over the neighborhood without extra lookups.
    /// </summary>
    public IReadOnlyList<string> CandidateLinkedMentionKeys { get; set; } = System.Array.Empty<string>();
}

/// <summary>
/// Produces zero or one signal for a candidate edge. Returning <c>null</c> means "no opinion" and
/// should not affect confidence.
/// </summary>
public interface ISignalProducer
{
    string Name { get; }          // e.g. "AliasMatcher@1"
    string Kind { get; }          // matches ResolutionPolicy.SignalWeights key, e.g. "aliasMatch"
    ResolutionSignal? Score(SignalContext ctx, ResolutionPolicy policy);
}
