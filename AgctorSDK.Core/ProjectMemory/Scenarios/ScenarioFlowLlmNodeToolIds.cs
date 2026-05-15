using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace AgctorSDK.Core.ProjectMemory.Scenarios;

/// <summary>
/// Parses portable scenario-flow <c>LlmNode.config</c> JSON: optional <c>toolIds</c> and <c>toolPreset</c> (PRD-014 extension).
/// </summary>
public static class ScenarioFlowLlmNodeToolIds
{
    public const string PersonMemoryContext = "person-memory-context";
    public const string ApplyMemoryIntents = "apply-memory-intents";

    /// <summary>Stable ids matching host HTTP tool primary ids in <c>AgctorToolCatalog</c>.</summary>
    public static IReadOnlyList<string> ParseFlowDeclaredToolIds(JsonElement? config)
    {
        var list = new List<string>();
        if (config is not { ValueKind: JsonValueKind.Object } el)
            return list;

        if (el.TryGetProperty("toolIds", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arr.EnumerateArray())
            {
                var s = item.GetString()?.Trim();
                if (!string.IsNullOrEmpty(s))
                    list.Add(s);
            }
        }

        if (el.TryGetProperty("toolPreset", out var presetEl))
        {
            var p = presetEl.GetString()?.Trim().ToLowerInvariant();
            switch (p)
            {
                case "person-memory-read":
                case "person_memory_read":
                    AddDistinct(list, PersonMemoryContext);
                    break;
                case "memory-write":
                case "memory_write":
                    AddDistinct(list, ApplyMemoryIntents);
                    break;
                case "person-memory-and-write":
                    AddDistinct(list, PersonMemoryContext);
                    AddDistinct(list, ApplyMemoryIntents);
                    break;
            }
        }

        return list;
    }

    /// <summary>Union of YAML tools.allow and flow-declared ids (case-insensitive).</summary>
    public static bool UnionAllows(IEnumerable<string> yamlAllow, IReadOnlyList<string> flowIds, string toolPrimaryId)
    {
        var needle = toolPrimaryId.Trim();
        foreach (var x in yamlAllow)
        {
            if (string.Equals(x?.Trim(), needle, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (var x in flowIds)
        {
            if (string.Equals(x?.Trim(), needle, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static void AddDistinct(List<string> list, string id)
    {
        if (list.Any(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase)))
            return;
        list.Add(id);
    }
}
