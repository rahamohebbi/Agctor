using System;
using System.Collections.Generic;

namespace AgctorSDK.Core.ProjectMemory.OutOfSchema;

/// <summary>How the host should treat a <c>route_miss</c> fact (PRD-019).</summary>
public enum OutOfSchemaDisposition
{
    /// <summary>Show a clear yes/no question in the same turn (high confidence).</summary>
    ImmediateConfirmation = 0,

    /// <summary>Persist to <c>.agctor/runtime/generic-inbox/pending.yaml</c> for later review.</summary>
    ReviewQueue = 1
}

/// <summary>One unrouted extractor intent surfaced for user confirmation.</summary>
public sealed class OutOfSchemaFactProposal
{
    public string ProposalId { get; init; } = "";

    public string EntityKey { get; init; } = "";

    public string KnowledgeType { get; init; } = "";

    public string? Attribute { get; init; }

    public string Value { get; init; } = "";

    public double Confidence { get; init; }

    public OutOfSchemaDisposition Disposition { get; init; }

    /// <summary>Ready-to-show line for the UI or assistant (no extra templating required).</summary>
    public string UserPromptLine { get; init; } = "";
}

/// <summary>Caller payload after the user approves storing unrouted facts in the generic inbox.</summary>
public sealed class ApprovedGenericFact
{
    public string ProposalId { get; init; } = "";

    public string EntityKey { get; init; } = "";

    public string KnowledgeType { get; init; } = "";

    public string? Attribute { get; init; }

    public string Value { get; init; } = "";

    public double Confidence { get; init; }
}

/// <summary>Result of <see cref="IGenericInboxStore.PersistApprovedAsync"/>.</summary>
public sealed class GenericInboxPersistResult
{
    public int Appended { get; init; }

    public int RejectedMismatch { get; init; }

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}
