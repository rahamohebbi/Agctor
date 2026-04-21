using System.Collections.Generic;
using AgctorSDK.Core.ProjectMemory.Resolution.Messages;
using AgctorSDK.Core.ProjectMemory.Resolution.Models;

namespace AgctorSDK.Core.ProjectMemory.Resolution.Trace;

/// <summary>
/// Shape of the <c>pm.playground.resolve</c> trace span's <c>timelineDetailJson</c> payload. Kept
/// as a plain DTO so the Host trace UI (PRD-016 Input / Outcome cards) can render it without
/// referencing actor internals. Three top-level sections — Input, Evidence, Outcome — mirror the
/// conventions in <c>PlaygroundTraceTimelineDetail</c>.
/// </summary>
public sealed class ResolveSpanDetail
{
    public ResolveSpanInput Input { get; set; } = new();
    public ResolveSpanEvidence Evidence { get; set; } = new();
    public ResolveSpanOutcome Outcome { get; set; } = new();

    /// <summary>Build a detail payload from the three core artifacts the actor already has.</summary>
    public static ResolveSpanDetail Build(
        ResolveCandidate request,
        ResolutionEdge edge,
        IReadOnlyList<LookupCandidate> allCandidates)
    {
        return new ResolveSpanDetail
        {
            Input = new ResolveSpanInput
            {
                MentionId = request.Mention.MentionId,
                Scope = request.Mention.Scope?.ToKey() ?? "project",
                SurfaceForm = request.Mention.SurfaceForm,
                CandidateEntityKey = request.CandidateEntityKey,
                AllCandidates = new List<ResolveSpanCandidate>(PackCandidates(allCandidates))
            },
            Evidence = new ResolveSpanEvidence
            {
                Confidence = edge.Confidence,
                Signals = new List<ResolveSpanSignal>(PackSignals(edge.Signals))
            },
            Outcome = new ResolveSpanOutcome
            {
                EdgeId = edge.EdgeId,
                State = edge.State.ToString(),
                PromotedBy = edge.Promotions.Count > 0 ? edge.Promotions[edge.Promotions.Count - 1].By : null
            }
        };
    }

    private static IEnumerable<ResolveSpanCandidate> PackCandidates(IReadOnlyList<LookupCandidate> cands)
    {
        if (cands == null) yield break;
        foreach (var c in cands)
            yield return new ResolveSpanCandidate
            {
                EntityKey = c.EntityKey,
                SurfaceScore = c.SurfaceScore,
                UniquenessScore = c.UniquenessScore
            };
    }

    private static IEnumerable<ResolveSpanSignal> PackSignals(IEnumerable<ResolutionSignal> signals)
    {
        if (signals == null) yield break;
        foreach (var s in signals)
            yield return new ResolveSpanSignal
            {
                Kind = s.Kind,
                Score = s.Score,
                Weight = s.Weight,
                Rationale = s.Rationale,
                ProducedBy = s.ProducedBy,
                IsNegative = s.IsNegative
            };
    }
}

public sealed class ResolveSpanInput
{
    public string MentionId { get; set; } = "";
    public string Scope { get; set; } = "";
    public string SurfaceForm { get; set; } = "";
    public string CandidateEntityKey { get; set; } = "";
    public List<ResolveSpanCandidate> AllCandidates { get; set; } = new();
}

public sealed class ResolveSpanCandidate
{
    public string EntityKey { get; set; } = "";
    public double SurfaceScore { get; set; }
    public double UniquenessScore { get; set; }
}

public sealed class ResolveSpanEvidence
{
    public double Confidence { get; set; }
    public List<ResolveSpanSignal> Signals { get; set; } = new();
}

public sealed class ResolveSpanSignal
{
    public string Kind { get; set; } = "";
    public double Score { get; set; }
    public double Weight { get; set; }
    public string Rationale { get; set; } = "";
    public string ProducedBy { get; set; } = "";
    public bool IsNegative { get; set; }
}

public sealed class ResolveSpanOutcome
{
    public string EdgeId { get; set; } = "";
    public string State { get; set; } = "";
    public string? PromotedBy { get; set; }
}
