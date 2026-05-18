using System.Text.Json;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.Tools.Implementations;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Scenarios;
using AgctorSDK.Core.ProjectMemory.Tools;
using AgctorSDK.Core.Tools.Models;

namespace AgctorSDK.Host.Services.ProjectMemory;

/// <summary>
/// Host-facing entry points for playground flow; implementation lives in Core (<see cref="PersonMemoryMarkdownContextBuilder"/>).
/// </summary>
public static class PlaygroundPersonQueryContextBuilder
{
    /// <summary>True when flow/YAML requests <c>person-memory-context</c> or persona is a known memory reader.</summary>
    public static bool ShouldLoadPersonMemoryContext(AgentDefinitionSpec spec, JsonElement? flowNodeConfig)
    {
        var flowToolIds = ScenarioFlowLlmNodeToolIds.ParseFlowDeclaredToolIds(flowNodeConfig);
        var yamlAllow = spec.Tools?.Allow ?? new List<string>();
        if (ScenarioFlowLlmNodeToolIds.UnionAllows(
                yamlAllow,
                flowToolIds,
                ScenarioFlowLlmNodeToolIds.PersonMemoryContext))
            return true;

        return string.Equals(spec.Id, "person-query", StringComparison.OrdinalIgnoreCase)
               || string.Equals(spec.Id, "relationship-coach", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Loads scenario people markdown appendix for flow LlmNodes (person-query, relationship-coach, …).</summary>
    public static async Task<string> BuildFlowAppendixAsync(
        IProjectLoader loader,
        IEntityRegistry entities,
        IAgentFactory agentFactory,
        AgentDefinitionSpec spec,
        string personaId,
        string projectRootFull,
        string? scenarioId,
        string contextStrategy,
        string userMessage,
        CancellationToken cancellationToken)
    {
        var toolReq = new ToolRequest
        {
            Operation = "BuildContext",
            Parameters = new Dictionary<string, object>
            {
                ["projectRoot"] = projectRootFull,
                ["scenarioId"] = scenarioId ?? "",
                ["contextStrategy"] = contextStrategy,
                ["userMessage"] = userMessage,
                ["agentSpecId"] = personaId.Trim()
            }
        };

        try
        {
            var tr = await agentFactory
                .InvokeToolRequestAsync(nameof(PersonMemoryContextTool), toolReq, null, cancellationToken)
                .ConfigureAwait(false);
            if (tr is { IsSuccess: true, Output: not null })
                return Convert.ToString(tr.Output) ?? "";
        }
        catch
        {
            /* fall through to direct markdown load */
        }

        var pmOps = new ProjectMemoryOperations(loader, entities);
        return await BuildAppendixAsync(
                pmOps,
                spec,
                projectRootFull,
                scenarioId,
                contextStrategy,
                userMessage,
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal const int MaxEntities = PersonMemoryMarkdownContextBuilder.MaxEntities;
    internal const int MaxMarkdownFilesPerEntity = PersonMemoryMarkdownContextBuilder.MaxMarkdownFilesPerEntity;
    internal const int MaxAppendixChars = PersonMemoryMarkdownContextBuilder.MaxAppendixChars;

    public static string ParseStrategy(JsonElement? config) =>
        PersonMemoryMarkdownContextBuilder.ParseStrategy(config);

    public static string? ExtractFocusQueryFromUserMessage(string? message) =>
        PersonMemoryMarkdownContextBuilder.ExtractFocusQueryFromUserMessage(message);

    public static Task<string> BuildAppendixAsync(
        ProjectMemoryOperations ops,
        AgentDefinitionSpec querySpec,
        string projectRootFull,
        string? scenarioId,
        string strategy,
        string focusSourceUserMessage,
        CancellationToken cancellationToken) =>
        PersonMemoryMarkdownContextBuilder.BuildAppendixAsync(
            ops,
            querySpec,
            projectRootFull,
            scenarioId,
            strategy,
            focusSourceUserMessage,
            cancellationToken,
            loadedViaLine:
            "Playground person-query: markdown below was read from disk (single-step LLM; use LlmNode toolIds to route via PersonMemoryContextTool).");
}
