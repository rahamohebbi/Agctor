using System.Collections.Generic;
using AgctorSDK.Core.ProjectMemory.Resolution.Models;

namespace AgctorSDK.Core.ProjectMemory.Resolution.Messages;

/// <summary>
/// Extractor observed a raw mention of something that may be an entity. First class input to the
/// reconciler mailbox. See PRD-018 §5.2.
/// </summary>
public sealed class MentionObserved
{
    public MentionRef Mention { get; set; } = new();
}

/// <summary>
/// Aggregated view of a session at close or checkpoint. Drives cross-session reconciliation.
/// </summary>
public sealed class SessionSummary
{
    public string SessionId { get; set; } = "";
    public string? ProjectId { get; set; }
    public List<MentionRef> Mentions { get; set; } = new();
    public List<string> AssertedFacts { get; set; } = new();
    public List<string> NegativeAssertions { get; set; } = new();
}

/// <summary>
/// Reconciler tells a specific resolution actor to consider linking <see cref="Mention"/> to
/// <see cref="CandidateEntityKey"/>, scoring via the configured signal producers. Candidate
/// identity fields (key, path) are required; the resolution actor owns richer metadata itself.
/// </summary>
public sealed class ResolveCandidate
{
    public MentionRef Mention { get; set; } = new();
    public string CandidateEntityKey { get; set; } = "";
    public string CandidateEntityPath { get; set; } = "";   // absolute path to the entity folder

    /// <summary>Total entities in the project whose aliases/display match the surface form (for S2).</summary>
    public int TotalEntitiesMatchingSurface { get; set; } = 1;

    /// <summary>Session-level facts and negations piped through for S3/S7 (optional).</summary>
    public List<string> SessionAssertedFacts { get; set; } = new();
    public List<string> SessionNegativeAssertions { get; set; } = new();
}

/// <summary>Resolution actor published a new piece of evidence.</summary>
public sealed class EvidenceAppended
{
    public string EdgeId { get; set; } = "";
    public string TargetEntityKey { get; set; } = "";
    public ResolutionEdgeState State { get; set; }
    public double Confidence { get; set; }
    public ResolutionSignal AddedSignal { get; set; } = new();
}

/// <summary>Resolution actor transitioned an edge's state.</summary>
public sealed class LinkStateChanged
{
    public string EdgeId { get; set; } = "";
    public string TargetEntityKey { get; set; } = "";
    public ResolutionEdgeState From { get; set; }
    public ResolutionEdgeState To { get; set; }
    public double ConfidenceSnapshot { get; set; }
    public string By { get; set; } = "auto";
}

/// <summary>Operator or system requests promotion (soft -&gt; hard).</summary>
public sealed class PromotionRequested
{
    public string EdgeId { get; set; } = "";
    public string RequestedBy { get; set; } = "auto"; // "auto" | "user:<id>"
    public string? Reason { get; set; }
}

/// <summary>Operator or system requests demotion or rejection.</summary>
public sealed class DemotionRequested
{
    public string EdgeId { get; set; } = "";
    public string RequestedBy { get; set; } = "auto";
    public string? Reason { get; set; }
    public bool Reject { get; set; }
}

/// <summary>
/// Hot-swap of resolution configuration without actor restart. Published when
/// <c>.agctor/resolution.yaml</c> changes; the reconciler re-weights in-flight work, and
/// resolution actors pick up the new thresholds for future edges.
/// </summary>
public sealed class ReloadPolicy
{
    public Models.ResolutionPolicy Policy { get; set; } = Models.ResolutionPolicy.CreateDefault();
    public string? ChangedBy { get; set; }
}

/// <summary>Ask the mention-index for entities matching a surface form.</summary>
public sealed class LookupBySurface
{
    public string Text { get; set; } = "";
    public ResolutionScope Scope { get; set; } = ResolutionScope.Project();
}

public sealed class LookupCandidate
{
    public string EntityKey { get; set; } = "";
    public string EntityPath { get; set; } = "";
    public double SurfaceScore { get; set; }                // alias match contribution
    public double UniquenessScore { get; set; }             // 1 / matching-count
}

public sealed class LookupResponse
{
    public List<LookupCandidate> Candidates { get; set; } = new();
}
