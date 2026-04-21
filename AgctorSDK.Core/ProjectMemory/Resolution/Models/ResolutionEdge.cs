using System;
using System.Collections.Generic;

namespace AgctorSDK.Core.ProjectMemory.Resolution.Models;

/// <summary>
/// Grade of confidence an edge currently has. Soft edges are hypotheses that queries can see but
/// aggregates should not trust; Hard edges are canonical. Rejected and Superseded are terminal-ish
/// states that still keep the evidence trail.
/// </summary>
public enum ResolutionEdgeState
{
    Soft,
    Hard,
    Rejected,
    Superseded
}

/// <summary>
/// Where a mention was observed: project-root scope or a specific scenario scope.
/// Resolution prefers scenario scope first and widens to project scope when needed.
/// </summary>
public sealed class ResolutionScope
{
    public string Kind { get; set; } = "project"; // "project" | "scenario"
    public string? ScenarioId { get; set; }

    public static ResolutionScope Project() => new() { Kind = "project" };
    public static ResolutionScope Scenario(string id) => new() { Kind = "scenario", ScenarioId = id };

    public string ToKey() => Kind == "scenario" && !string.IsNullOrWhiteSpace(ScenarioId)
        ? $"scenario:{ScenarioId}"
        : "project";
}

/// <summary>
/// A single raw reference to an entity in a document or turn. Identified by a stable path so the
/// same mention re-observed does not duplicate evidence.
/// </summary>
public sealed class MentionRef
{
    public string MentionId { get; set; } = "";           // stable, e.g. "scenario:s1:ryan#relationships.family[0]"
    public ResolutionScope Scope { get; set; } = ResolutionScope.Project();
    public string SurfaceForm { get; set; } = "";         // raw text as seen, e.g. "Raha"
    public string? WithinEntityKey { get; set; }          // host entity folder key if the mention lives inside another entity
    public string? SourcePath { get; set; }               // relative path (e.g. "people/ryan/relationships.md")
    public string? Field { get; set; }                    // logical field, e.g. "relationships.family[0]"
    public string? SessionId { get; set; }
    public string? TurnId { get; set; }
}

/// <summary>
/// One scored signal contributed by a signal producer. Signals are append-only in the edge log; a
/// fresh signal with the same (edgeId, kind, inputsFingerprint) is idempotent.
/// </summary>
public sealed class ResolutionSignal
{
    public string Kind { get; set; } = "";                // aliasMatch, uniqueness, corefInSession, attrOverlap, embedding, graphConsistency, negative
    public double Score { get; set; }                     // [0, 1]
    public double Weight { get; set; }                    // [0, 1] copied from policy at the time of scoring
    public string Rationale { get; set; } = "";
    public string ProducedBy { get; set; } = "";          // producer name + version, e.g. "AliasMatcher@1"
    public string InputsFingerprint { get; set; } = "";   // sha256 of inputs; idempotency key
    public DateTimeOffset ObservedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsNegative { get; set; }                  // true for negative-evidence signals
}

/// <summary>
/// One row in the promotion audit log. Captures *why* the state flipped at a specific instant,
/// including a frozen signal snapshot so the decision remains reproducible even after weights change.
/// </summary>
public sealed class ResolutionPromotion
{
    public ResolutionEdgeState From { get; set; }
    public ResolutionEdgeState To { get; set; }
    public string By { get; set; } = "";                  // "auto" | "user:<id>"
    public string? Reason { get; set; }
    public double ConfidenceSnapshot { get; set; }
    public double ThresholdUsed { get; set; }
    public List<ResolutionSignal> SignalsSnapshot { get; set; } = new();
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// A mention -> entity edge with confidence, evidence, and transition history. Owned on disk by
/// the target entity's <c>.resolution/incoming.yaml</c>. See PRD-018 §5.5.
/// </summary>
public sealed class ResolutionEdge
{
    public string EdgeId { get; set; } = "";              // stable across re-observations; see MakeEdgeId
    public string TargetEntityKey { get; set; } = "";     // canonical folder key the edge points at
    public MentionRef Mention { get; set; } = new();
    public ResolutionEdgeState State { get; set; } = ResolutionEdgeState.Soft;
    public double Confidence { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastUpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<ResolutionSignal> Signals { get; set; } = new();
    public List<ResolutionPromotion> Promotions { get; set; } = new();

    /// <summary>
    /// Build a stable edge id. Deterministic so the same mention re-observed updates the same row
    /// instead of creating a duplicate.
    /// </summary>
    public static string MakeEdgeId(MentionRef mention, string targetEntityKey)
    {
        var scope = mention.Scope?.ToKey() ?? "project";
        var mid = string.IsNullOrWhiteSpace(mention.MentionId) ? $"{scope}:{mention.SurfaceForm}" : mention.MentionId;
        return $"{mid}->entity:{targetEntityKey}";
    }
}
