using AgctorSDK.Core.ProjectMemory.Scenarios;

namespace AgctorSDK.Host.Services.Scenarios;

/// <summary>PRD-024 outcome of one segment pass through the flow graph.</summary>
public sealed class ScenarioFlowSegmentRunResult
{
    public ScenarioFlowSegmentOutcome Outcome { get; init; }

    public ScenarioFlowRuntimeSnapshot Snapshot { get; init; } = new();

    public string? Output { get; init; }

    public string? ErrorMessage { get; init; }

    public static ScenarioFlowSegmentRunResult WaitForInput(ScenarioFlowRuntimeSnapshot snapshot) =>
        new() { Outcome = ScenarioFlowSegmentOutcome.SuspendedWaitForInput, Snapshot = snapshot };

    public static ScenarioFlowSegmentRunResult AwaitEvent(ScenarioFlowRuntimeSnapshot snapshot) =>
        new() { Outcome = ScenarioFlowSegmentOutcome.SuspendedAwaitEvent, Snapshot = snapshot };

    public static ScenarioFlowSegmentRunResult Completed(ScenarioFlowRuntimeSnapshot snapshot, string output) =>
        new() { Outcome = ScenarioFlowSegmentOutcome.Completed, Snapshot = snapshot, Output = output };

    public static ScenarioFlowSegmentRunResult Failed(ScenarioFlowRuntimeSnapshot snapshot, string error) =>
        new() { Outcome = ScenarioFlowSegmentOutcome.Failed, Snapshot = snapshot, ErrorMessage = error };
}
