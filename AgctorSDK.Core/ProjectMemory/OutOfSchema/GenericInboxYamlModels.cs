using System.Collections.Generic;

namespace AgctorSDK.Core.ProjectMemory.OutOfSchema;

/// <summary>On-disk shape for <c>.agctor/runtime/generic-inbox/pending.yaml</c>.</summary>
public sealed class GenericInboxPendingFile
{
    public int SchemaVersion { get; set; } = 1;

    public List<GenericInboxPendingRow> Items { get; set; } = new();
}

public sealed class GenericInboxPendingRow
{
    public string ProposalId { get; set; } = "";

    public string EntityKey { get; set; } = "";

    public string KnowledgeType { get; set; } = "";

    public string? Attribute { get; set; }

    public string Value { get; set; } = "";

    public double Confidence { get; set; }

    public string Disposition { get; set; } = "review";

    /// <summary>Sanitized scenario folder segment when scoped; empty for project-root workspace.</summary>
    public string ScenarioSegment { get; set; } = "";

    public string QueuedAtUtc { get; set; } = "";

    public string UserPromptLine { get; set; } = "";

    /// <summary>Related photo asset id when proposal came from vision extract (PRD-023).</summary>
    public string? SourceAssetId { get; set; }
}

/// <summary>On-disk shape for <c>.agctor/runtime/generic-inbox/confirmed.yaml</c>.</summary>
public sealed class GenericInboxConfirmedFile
{
    public int SchemaVersion { get; set; } = 1;

    public List<GenericInboxConfirmedRow> Items { get; set; } = new();
}

public sealed class GenericInboxConfirmedRow
{
    public string ProposalId { get; set; } = "";

    public string EntityKey { get; set; } = "";

    public string KnowledgeType { get; set; } = "";

    public string? Attribute { get; set; }

    public string Value { get; set; } = "";

    public double Confidence { get; set; }

    public string ScenarioSegment { get; set; } = "";

    public string Source { get; set; } = "user_approved";

    public string CapturedAtUtc { get; set; } = "";

    /// <summary>UTC instant the row was successfully back-projected to entity files; null when never replayed (or still out-of-schema).</summary>
    public string? ReplayedAtUtc { get; set; }
}
