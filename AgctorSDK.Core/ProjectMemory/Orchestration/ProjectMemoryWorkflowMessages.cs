using System.Collections.Generic;
using AgctorSDK.Core.ProjectMemory.OutOfSchema;

namespace AgctorSDK.Core.ProjectMemory.Orchestration;

/// <summary>
/// Actor protocol request for ProjectMemory workflow execution. The request
/// wraps the existing pipeline model so the first actor boundary is behavior
/// preserving while later phases split the workflow into child actors.
/// </summary>
public sealed record ProjectMemoryWorkflowRequest(ProjectMemoryPipelineRequest PipelineRequest);

/// <summary>
/// Actor protocol response for ProjectMemory workflow execution.
/// </summary>
public sealed record ProjectMemoryWorkflowResult(ProjectMemoryPipelineResult PipelineResult);

/// <summary>
/// Actor protocol event for an observable ProjectMemory workflow step.
/// </summary>
public sealed record ProjectMemoryStepCompleted(ProjectMemoryPipelineStep Step);

/// <summary>
/// Actor protocol error returned when the workflow actor cannot execute a request.
/// </summary>
public sealed record ProjectMemoryWorkflowFailed(string Message);

/// <summary>
/// Actor protocol request for running only the ProjectMemory extractor prompt.
/// </summary>
public sealed record ProjectMemoryExtractWorkflowRequest(
    string ProjectRoot,
    string UserMessage,
    string? ConversationPrefix = null);

/// <summary>
/// Actor protocol response for extractor output.
/// </summary>
public sealed record ProjectMemoryExtractWorkflowResult(string RawExtractorLlmText, string Prompt);

/// <summary>
/// Actor protocol request for applying raw person-extractor output through the
/// existing ingest path.
/// </summary>
public sealed record ProjectMemoryIngestWorkflowRequest(
    string ProjectRoot,
    string? ScenarioId,
    string RawExtractorLlmText);

/// <summary>
/// Actor protocol response for raw extractor ingest.
/// </summary>
public sealed record ProjectMemoryIngestWorkflowResult(ProjectMemoryIngestResult IngestResult);

/// <summary>
/// Actor protocol request for running only the ProjectMemory query prompt.
/// </summary>
public sealed record ProjectMemoryQueryWorkflowRequest(
    string ProjectRoot,
    string UserMessage,
    string? ConversationPrefix = null,
    string? ScenarioId = null);

/// <summary>
/// Actor protocol response for query output.
/// </summary>
public sealed record ProjectMemoryQueryWorkflowResult(string Answer, string Prompt);

/// <summary>
/// Actor protocol request for persisting user-approved generic inbox facts.
/// </summary>
public sealed record ProjectMemoryGenericInboxPersistRequest(
    string ProjectRoot,
    string? ScenarioId,
    IReadOnlyList<ApprovedGenericFact> Approvals);

/// <summary>
/// Actor protocol response for generic inbox persistence.
/// </summary>
public sealed record ProjectMemoryGenericInboxPersistResult(GenericInboxPersistResult PersistResult);

