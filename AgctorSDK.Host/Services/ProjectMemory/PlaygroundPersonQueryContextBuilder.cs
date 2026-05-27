using System.Text.Json;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Visual;
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
    /// <summary>True when flow/YAML requests <c>person-visual-context</c> or persona is a visual reader.</summary>
    public static bool ShouldLoadPersonVisualContext(AgentDefinitionSpec spec, JsonElement? flowNodeConfig)
    {
        var flowToolIds = ScenarioFlowLlmNodeToolIds.ParseFlowDeclaredToolIds(flowNodeConfig);
        var yamlAllow = spec.Tools?.Allow ?? new List<string>();
        if (ScenarioFlowLlmNodeToolIds.UnionAllows(
                yamlAllow,
                flowToolIds,
                ScenarioFlowLlmNodeToolIds.PersonVisualContext))
            return true;

        return string.Equals(spec.Id, "style-coach", StringComparison.OrdinalIgnoreCase)
               || string.Equals(spec.Id, "fitness-coach", StringComparison.OrdinalIgnoreCase)
               || string.Equals(spec.Id, "person-query", StringComparison.OrdinalIgnoreCase)
               || string.Equals(spec.Id, "relationship-coach", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Maps persona + user text to PersonVisualContextTool intent (style/fitness/general).</summary>
    public static string InferVisualIntent(string personaId, string? userMessage)
    {
        if (string.Equals(personaId, "style-coach", StringComparison.OrdinalIgnoreCase))
            return "style";
        if (string.Equals(personaId, "fitness-coach", StringComparison.OrdinalIgnoreCase))
            return "fitness";
        return PlaygroundFlowPreRouter.InferSuggestedIntent(userMessage) switch
        {
            "style" => "style",
            "fitness" => "fitness",
            _ => "general"
        };
    }

    /// <summary>Loads scenario visual catalog appendix for flow LlmNodes (style-coach, fitness-coach, …).</summary>
    public static async Task<PersonVisualContextResult> BuildVisualContextAsync(
        IAgentFactory agentFactory,
        AgentDefinitionSpec spec,
        string personaId,
        string projectRootFull,
        string? scenarioId,
        string userMessage,
        string? focusEntityKey,
        int maxVisualPhotos,
        CancellationToken cancellationToken)
    {
        var intent = InferVisualIntent(personaId, userMessage);
        var entityKeys = string.IsNullOrWhiteSpace(focusEntityKey) ? null : focusEntityKey.Trim();
        var toolReq = new ToolRequest
        {
            Operation = "BuildContext",
            Parameters = new Dictionary<string, object>
            {
                ["projectRoot"] = projectRootFull,
                ["scenarioId"] = scenarioId ?? "",
                ["visualIntent"] = intent,
                ["userMessage"] = userMessage,
                ["maxAssets"] = maxVisualPhotos
            }
        };
        if (!string.IsNullOrWhiteSpace(entityKeys))
            toolReq.Parameters["entityKeys"] = entityKeys;

        try
        {
            var tr = await agentFactory
                .InvokeToolRequestAsync(nameof(PersonVisualContextTool), toolReq, null, cancellationToken)
                .ConfigureAwait(false);
            if (tr is { IsSuccess: true, Output: not null })
            {
                var json = Convert.ToString(tr.Output) ?? "";
                if (!string.IsNullOrWhiteSpace(json))
                {
                    using var doc = JsonDocument.Parse(json);
                    var appendix = doc.RootElement.TryGetProperty("appendix", out var appendixEl)
                        ? appendixEl.GetString() ?? ""
                        : "";
                    var assets = new List<PersonVisualContextAsset>();
                    if (doc.RootElement.TryGetProperty("assets", out var assetsEl)
                        && assetsEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in assetsEl.EnumerateArray())
                        {
                            assets.Add(new PersonVisualContextAsset
                            {
                                AssetId = item.TryGetProperty("assetId", out var idEl) ? idEl.GetString() ?? "" : "",
                                SceneSummary = item.TryGetProperty("sceneSummary", out var sceneEl)
                                    ? sceneEl.GetString()
                                    : null,
                                Caption = item.TryGetProperty("caption", out var capEl) ? capEl.GetString() : null
                            });
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(appendix))
                        return new PersonVisualContextResult(appendix, assets);
                }
            }
        }
        catch
        {
            /* fall through */
        }

        return new PersonVisualContextResult("---\nPerson-visual context failed to load.\n", Array.Empty<PersonVisualContextAsset>());
    }

    public static async Task<string> BuildVisualAppendixAsync(
        IAgentFactory agentFactory,
        AgentDefinitionSpec spec,
        string personaId,
        string projectRootFull,
        string? scenarioId,
        string userMessage,
        string? focusEntityKey,
        int maxVisualPhotos,
        CancellationToken cancellationToken)
    {
        var result = await BuildVisualContextAsync(
            agentFactory,
            spec,
            personaId,
            projectRootFull,
            scenarioId,
            userMessage,
            focusEntityKey,
            maxVisualPhotos,
            cancellationToken).ConfigureAwait(false);
        return result.Appendix;
    }

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
