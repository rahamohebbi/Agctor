using System.Collections.Generic;
using AgctorSDK.Core.ProjectMemory.OutOfSchema;

namespace AgctorSDK.Core.ProjectMemory.Orchestration;

/// <summary>Deterministic routing for PRD-013 pipeline MVP (no LLM planner).</summary>
public enum ProjectMemoryPipelineMode
{
    /// <summary>Extract → route/write if intents → query (answer uses updated files).</summary>
    Auto = 0,

    /// <summary>Extract → route → write only (no person-query LLM).</summary>
    IngestOnly = 1,

    /// <summary>person-query only (no extract/curator).</summary>
    QueryOnly = 2
}

/// <summary>Input for <see cref="IProjectMemoryPipelineRunner"/>.</summary>
public sealed class ProjectMemoryPipelineRequest
{
    /// <summary>Absolute path to project root (must contain <c>.agctor</c>).</summary>
    public string ProjectRoot { get; set; } = "";

    public string UserMessage { get; set; } = "";

    /// <summary>Stable id for logs and client correlation; generated if empty.</summary>
    public string CorrelationId { get; set; } = "";

    public ProjectMemoryPipelineMode Mode { get; set; } = ProjectMemoryPipelineMode.Auto;

    /// <summary>Optional transcript prefix for extract/query prompts (e.g. from session store).</summary>
    public string? ConversationPrefix { get; set; }

    /// <summary>When set, entity I/O uses <c>{ProjectRoot}/scenarios/&lt;sanitized&gt;/people/…</c> instead of project-root <c>people/…</c>.</summary>
    public string? ScenarioId { get; set; }

    /// <summary>Optional session id used by the PRD-018 resolution subsystem to correlate mentions.</summary>
    public string? SessionId { get; set; }

    /// <summary>Optional turn id used by the PRD-018 resolution subsystem for trace stitching.</summary>
    public string? TurnId { get; set; }
}

/// <summary>Ordered trace of pipeline steps + final assistant text.</summary>
public sealed class ProjectMemoryPipelineResult
{
    public string CorrelationId { get; set; } = "";

    /// <summary>True when no failed critical step for the selected mode.</summary>
    public bool Success { get; set; }

    /// <summary>Final user-visible text (from person-query in Auto/QueryOnly, or summary in IngestOnly).</summary>
    public string FinalText { get; set; } = "";

    public IReadOnlyList<ProjectMemoryPipelineStep> Steps { get; set; } = System.Array.Empty<ProjectMemoryPipelineStep>();
}

/// <summary>One observable step (extract, route, write, query).</summary>
public sealed class ProjectMemoryPipelineStep
{
    public string Name { get; set; } = "";

    public bool Ok { get; set; }

    /// <summary>Human-readable detail, error, or short LLM preview.</summary>
    public string? Detail { get; set; }

    public IReadOnlyList<string>? UpdatedFiles { get; set; }
}

/// <summary>Outcome of applying pre-generated person-extractor JSON (e.g. after PRD-014 <c>LlmNode</c> LLM).</summary>
public sealed class ProjectMemoryIngestResult
{
    /// <summary>True when <c>memoryIntents</c> JSON parsed.</summary>
    public bool ParseSuccess { get; init; }

    /// <summary>True when at least one markdown file was written.</summary>
    public bool WroteAnyFile { get; init; }

    public IReadOnlyList<string> UpdatedFiles { get; init; } = System.Array.Empty<string>();

    /// <summary>Parse/route/write notes or errors for operators.</summary>
    public string? Summary { get; init; }

    /// <summary>Which JSON shape produced ingest intents (e.g. legacy.memoryIntents or actionIntents.memory.persist).</summary>
    public string? ParseSource { get; init; }

    /// <summary>Unrouted intents the user may confirm for generic inbox storage (PRD-019).</summary>
    public IReadOnlyList<OutOfSchemaFactProposal> OutOfSchemaProposals { get; init; } = System.Array.Empty<OutOfSchemaFactProposal>();
}
