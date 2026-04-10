using System.Collections.Generic;

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
