namespace AgctorSDK.Core.ProjectMemory.Scenarios.Messages;

/// <summary>PRD-024: start or replace a flow run from ChatInput.</summary>
public sealed record ScenarioFlowStartMessage(
    string SessionId,
    string ScenarioId,
    string ProjectRoot,
    string FlowId,
    string FlowJson,
    string UserMessage,
    IReadOnlyList<string> AttachmentIds,
    string CorrelationId);

/// <summary>Resume after <c>WaitForInput</c> with user text and optional attachments.</summary>
public sealed record ScenarioFlowResumeUserInputMessage(
    string SessionId,
    string ScenarioId,
    string ProjectRoot,
    string FlowId,
    string FlowJson,
    string UserMessage,
    IReadOnlyList<string> AttachmentIds,
    string CorrelationId);

/// <summary>Resume after <c>AwaitEvent</c> when a domain actor publishes completion.</summary>
public sealed record ScenarioFlowResumeDomainEventMessage(
    string SessionId,
    string ScenarioId,
    string ProjectRoot,
    string FlowId,
    string FlowJson,
    string EventType,
    IReadOnlyDictionary<string, object?> Payload,
    string CorrelationId);

/// <summary>Clear snapshot and return to Idle.</summary>
public sealed record ScenarioFlowCancelMessage(
    string SessionId,
    string ScenarioId,
    string ProjectRoot,
    string? Reason);

/// <summary>Actor response for any runtime message.</summary>
public sealed record ScenarioFlowRuntimeResult(
    bool Success,
    bool Completed,
    ScenarioFlowRuntimeStatus Status,
    string ExecutionNodeId,
    string? Output,
    string? PendingPrompt,
    string? ErrorMessage);
