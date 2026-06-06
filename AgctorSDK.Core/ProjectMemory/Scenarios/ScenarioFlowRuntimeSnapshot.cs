namespace AgctorSDK.Core.ProjectMemory.Scenarios;

/// <summary>PRD-024 persisted execution snapshot (one per session + applied scenario).</summary>
public sealed class ScenarioFlowRuntimeSnapshot
{
    public string SchemaVersion { get; set; } = "1.0";

    public string FlowId { get; set; } = string.Empty;

    /// <summary>Node id where execution is active or suspended (not "cursor").</summary>
    public string ExecutionNodeId { get; set; } = string.Empty;

    public ScenarioFlowRuntimeStatus Status { get; set; } = ScenarioFlowRuntimeStatus.Idle;

    public ScenarioFlowRuntimeStoreState Store { get; set; } = new();

    public List<ScenarioFlowLoopRegionState> LoopRegions { get; set; } = new();

    public string? PendingPrompt { get; set; }

    public ScenarioFlowAwaitingEventState? AwaitingEvent { get; set; }

    public string? LastOutput { get; set; }

    public string? FailureReason { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class ScenarioFlowRuntimeStoreState
{
    public Dictionary<string, ScenarioFlowNodeOutputState> NodeOutputs { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, object?> Facts { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public ScenarioFlowAttachmentState Attachments { get; set; } = new();
}

public sealed class ScenarioFlowNodeOutputState
{
    public string Text { get; set; } = string.Empty;

    /// <summary>run | iteration | session — controls loop invalidation.</summary>
    public string Scope { get; set; } = "run";
}

public sealed class ScenarioFlowAttachmentState
{
    /// <summary>Asset ids uploaded since the last resume (delta ingest).</summary>
    public List<string> NewSinceLastResume { get; set; } = new();

    public List<string> AllInRun { get; set; } = new();
}

public sealed class ScenarioFlowLoopRegionState
{
    public string RegionId { get; set; } = string.Empty;

    public int Attempt { get; set; }

    public int MaxAttempts { get; set; }
}

public sealed class ScenarioFlowAwaitingEventState
{
    public string EventType { get; set; } = string.Empty;

    public DateTimeOffset? TimeoutAtUtc { get; set; }

    public string? CorrelationKey { get; set; }
}
