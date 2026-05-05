namespace AgctorSDK.Host.Services.ProjectMemory;

/// <summary>
/// Runs one project-memory YAML agent turn (playground-equivalent: prompt + local Ollama generate).
/// Shared by <see cref="Controllers.ProjectMemoryController"/> and PRD-014 scenario flow <c>LlmNode</c> execution.
/// </summary>
public interface IProjectMemoryPersonaLlmRunner
{
    /// <summary>
    /// Loads specs from <paramref name="projectRoot"/>, resolves <paramref name="agentId"/>, builds prompt (optional session transcript), calls LLM once.
    /// </summary>
    /// <param name="scenarioId">When set, prompt notes scenario-scoped persona paths under <c>scenarios/&lt;id&gt;/people/</c>.</param>
    Task<ProjectMemoryPersonaRunResult> RunAsync(
        string projectRoot,
        string? sessionId,
        string agentId,
        string inputText,
        CancellationToken cancellationToken = default,
        string? scenarioId = null);
}

/// <summary>Outcome of a single persona / YAML-agent LLM invocation.</summary>
/// <param name="IngestedFilePaths">Populated when person-extractor JSON was applied to disk under a scoped scenario.</param>
/// <param name="IngestSummary">Parse/route/write notes when ingest ran or was attempted.</param>
public sealed record ProjectMemoryPersonaRunResult(
    bool Ok,
    string? ErrorMessage,
    string? OutputText,
    System.Collections.Generic.IReadOnlyList<string>? IngestedFilePaths = null,
    string? IngestSummary = null);
