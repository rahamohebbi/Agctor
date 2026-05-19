using System.Collections.Generic;
using AgctorSDK.Core.ProjectMemory.LifeSignals;

namespace AgctorSDK.Core.ProjectMemory.Companion;

/// <summary>Why the session-end ingest actor was invoked.</summary>
public enum SessionEndIngestTrigger
{
    Checkpoint,
    Delete
}

/// <summary>Actor protocol: ingest new transcript turns when a session ends or checkpoints.</summary>
public sealed record SessionEndIngestWorkflowRequest(
    string SessionId,
    string ProjectRoot,
    string? ScenarioId,
    SessionEndIngestTrigger Trigger);

/// <summary>Actor protocol response for session-end ingest.</summary>
public sealed record SessionEndIngestWorkflowResult(
    bool Success,
    bool Skipped,
    string? SkipReason,
    string? CorrelationId,
    string? FinalTextSnippet,
    int LastIncludedSequence);

/// <summary>Public service result (mirrors actor response).</summary>
public sealed record SessionEndIngestResult(
    bool Success,
    bool Skipped,
    string? SkipReason,
    string? CorrelationId,
    string? FinalTextSnippet,
    int LastIncludedSequence);

/// <summary>Actor protocol: scan people markdown for proactive nudges.</summary>
public sealed record ProactiveSignalsWorkflowRequest(
    string ProjectRoot,
    string? ScenarioId,
    int StaleContactDays = 30,
    int BirthdayHorizonDays = 14);

/// <summary>Actor protocol response for proactive signals.</summary>
public sealed record ProactiveSignalsWorkflowResult(IReadOnlyList<PersonLifeSignal> Signals);
