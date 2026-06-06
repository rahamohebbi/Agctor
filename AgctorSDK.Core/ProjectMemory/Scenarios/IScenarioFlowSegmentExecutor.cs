namespace AgctorSDK.Core.ProjectMemory.Scenarios;

/// <summary>Outcome of one segment execution pass (Host implements via graph runner).</summary>
public enum ScenarioFlowSegmentOutcome
{
    Completed,
    SuspendedWaitForInput,
    SuspendedAwaitEvent,
    Failed
}

public sealed class ScenarioFlowSegmentRequest
{
    public required string ProjectRoot { get; init; }

    public required string ScenarioId { get; init; }

    public required string SessionId { get; init; }

    public required string UserMessage { get; init; }

    public IReadOnlyList<string> AttachmentIds { get; init; } = Array.Empty<string>();

    public required ScenarioFlowRuntimeSnapshot Snapshot { get; init; }

    /// <summary>Portable flow graph JSON (Host deserializes to GraphDocument).</summary>
    public required string FlowJson { get; init; }

    public TimeSpan LlmNodeTimeout { get; init; } = TimeSpan.FromSeconds(600);
}

public sealed class ScenarioFlowSegmentResult
{
    public ScenarioFlowSegmentOutcome Outcome { get; init; }

    public required ScenarioFlowRuntimeSnapshot Snapshot { get; init; }

    public string? Output { get; init; }

    public string? ErrorMessage { get; init; }
}

/// <summary>Executes one graph segment from <see cref="ScenarioFlowRuntimeSnapshot.ExecutionNodeId"/>.</summary>
public interface IScenarioFlowSegmentExecutor
{
    Task<ScenarioFlowSegmentResult> RunSegmentAsync(
        ScenarioFlowSegmentRequest request,
        CancellationToken cancellationToken = default);
}
