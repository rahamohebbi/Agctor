using System.Text.Json;

namespace AgctorSDK.Host.Services.Scenarios;

/// <summary>Resolves and orders multi-branch router execution (parallel vs sequential).</summary>
public static class ScenarioFlowBranchExecutionPlanner
{
    /// <summary>Personas that persist or ingest — run before read/query branches when sequential.</summary>
    private static readonly string[] WriteFirstPersonas =
    {
        "person-extractor",
        "visual-intake",
        "memory-curator"
    };

    /// <summary>Personas that read memory to answer — run after write branches when sequential.</summary>
    private static readonly string[] ReadLastPersonas =
    {
        "person-query",
        "relationship-coach",
        "style-coach",
        "fitness-coach"
    };

    public static ScenarioFlowRouterBranchExecution Resolve(
        ScenarioFlowRouterConfig config,
        ScenarioFlowRouterBranchExecution? llmResolved,
        IReadOnlyList<string> selectedPersonaIds)
    {
        if (config.TargetPolicy == ScenarioFlowRouterTargetPolicy.SingleBest || selectedPersonaIds.Count <= 1)
            return ScenarioFlowRouterBranchExecution.Sequential;

        return config.BranchExecution switch
        {
            ScenarioFlowRouterBranchExecution.Sequential => ScenarioFlowRouterBranchExecution.Sequential,
            ScenarioFlowRouterBranchExecution.Parallel => ScenarioFlowRouterBranchExecution.Parallel,
            ScenarioFlowRouterBranchExecution.Auto => llmResolved ?? InferAuto(selectedPersonaIds),
            _ => ScenarioFlowRouterBranchExecution.Parallel
        };
    }

    /// <summary>Heuristic when auto mode omits <c>branchExecutionMode</c>.</summary>
    public static ScenarioFlowRouterBranchExecution InferAuto(IReadOnlyList<string> selectedPersonaIds)
    {
        var set = new HashSet<string>(selectedPersonaIds, StringComparer.OrdinalIgnoreCase);
        var hasWrite = set.Any(p =>
            WriteFirstPersonas.Any(w => string.Equals(w, p, StringComparison.OrdinalIgnoreCase)));
        var hasRead = set.Any(p =>
            ReadLastPersonas.Any(r => string.Equals(r, p, StringComparison.OrdinalIgnoreCase)));
        return hasWrite && hasRead
            ? ScenarioFlowRouterBranchExecution.Sequential
            : ScenarioFlowRouterBranchExecution.Parallel;
    }

    public static IReadOnlyList<string> OrderBranchStarts(
        ScenarioFlowDocument flow,
        IReadOnlyDictionary<string, ScenarioFlowNode> map,
        IReadOnlyList<string> branchStartNodeIds)
    {
        return branchStartNodeIds
            .OrderBy(id => BranchOrderRank(TryGetPersonaId(map, id)))
            .ThenBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static int BranchOrderRank(string? personaId)
    {
        if (string.IsNullOrWhiteSpace(personaId))
            return 100;

        for (var i = 0; i < WriteFirstPersonas.Length; i++)
        {
            if (string.Equals(WriteFirstPersonas[i], personaId, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        for (var j = 0; j < ReadLastPersonas.Length; j++)
        {
            if (string.Equals(ReadLastPersonas[j], personaId, StringComparison.OrdinalIgnoreCase))
                return 50 + j;
        }

        return 40;
    }

    private static string? TryGetPersonaId(IReadOnlyDictionary<string, ScenarioFlowNode> map, string nodeId)
    {
        if (!map.TryGetValue(nodeId.Trim(), out var node))
            return null;
        return TryGetPersonaId(node.Config);
    }

    private static string? TryGetPersonaId(JsonElement? config)
    {
        if (config is not { ValueKind: JsonValueKind.Object } el)
            return null;
        return el.TryGetProperty("personaId", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()?.Trim()
            : null;
    }
}
