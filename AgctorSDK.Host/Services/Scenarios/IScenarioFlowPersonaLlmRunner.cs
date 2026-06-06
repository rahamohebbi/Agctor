using System.Text.Json;
using AgctorSDK.Core.ProjectMemory.Scenarios;
using AgctorSDK.Host.Services.ProjectMemory;

namespace AgctorSDK.Host.Services.Scenarios;

/// <summary>
/// Persona LLM for PRD-024 flow segments — loads person-memory and person-visual-context like the playground stream.
/// </summary>
public interface IScenarioFlowPersonaLlmRunner
{
    Task<ProjectMemoryPersonaRunResult> RunFlowNodeAsync(
        ScenarioFlowPersonaRunRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>One LlmNode invocation inside a scenario-flow segment.</summary>
public sealed class ScenarioFlowPersonaRunRequest
{
    public required string ProjectRoot { get; init; }

    public required string ScenarioId { get; init; }

    public string? SessionId { get; init; }

    public required string AgentId { get; init; }

    public required string InputText { get; init; }

    public string? FlowNodeId { get; init; }

    public JsonElement? FlowNodeConfig { get; init; }

    public ScenarioFlowRuntimeSnapshot? Snapshot { get; init; }

    /// <summary>When true, load visual catalog without entity filter (post-extract style pass).</summary>
    public bool RelaxVisualEntityFilter { get; init; }

    /// <summary>When set, visual context is limited to this session's uploaded asset ids (all photos in one pass).</summary>
    public IReadOnlyList<string>? SessionAssetIds { get; init; }
}
