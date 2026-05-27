using System.Text;
using System.Text.Json;

namespace AgctorSDK.Host.Services.Scenarios;

/// <summary>
/// Applies scenario <c>outputPolicy</c> when combining parallel branch text at Merge/Output.
/// Without this, Merge only joins strings and internal personas (e.g. memory-curator) can dominate the user reply.
/// </summary>
public static class ScenarioFlowOutputComposer
{
    /// <summary>Lower index = higher priority for <c>ranked</c> user-facing replies.</summary>
    private static readonly string[] RankedPersonaPriority =
    {
        "person-query",
        "relationship-coach",
        "style-coach",
        "fitness-coach",
        "visual-intake",
        "person-extractor",
        "memory-curator"
    };

    public sealed record Section(string? PersonaId, string Label, string Text);

    public static string Compose(
        ScenarioFlowDocument flow,
        IReadOnlyDictionary<string, ScenarioFlowNode> map,
        IReadOnlyDictionary<string, string> store,
        string toNodeId,
        string? outputPolicy)
    {
        var sections = CollectSections(flow, map, store, toNodeId);
        if (sections.Count == 0)
            return string.Empty;

        var policy = NormalizePolicy(outputPolicy);
        return policy switch
        {
            "first_non_empty" => sections.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Text))?.Text ?? "",
            "ranked" => ComposeRanked(sections),
            _ => ComposeMergeSections(sections)
        };
    }

    /// <summary>Best persona to label the playground transcript when several branches ran.</summary>
    public static string? PickTranscriptPersonaId(IEnumerable<Section> sections) =>
        PickTranscriptPersonaId(sections.Select(s => s.PersonaId));

    public static string? PickTranscriptPersonaId(IEnumerable<string?> personaIds)
    {
        var seen = personaIds
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (seen.Count == 0)
            return null;

        foreach (var pid in RankedPersonaPriority)
        {
            if (seen.Any(s => string.Equals(s, pid, StringComparison.OrdinalIgnoreCase)))
                return pid;
        }

        return seen[0];
    }

    public static IReadOnlyList<Section> CollectSections(
        ScenarioFlowDocument flow,
        IReadOnlyDictionary<string, ScenarioFlowNode> map,
        IReadOnlyDictionary<string, string> store,
        string toNodeId)
    {
        var list = new List<Section>();
        foreach (var e in (flow.Edges ?? new List<ScenarioFlowEdge>())
                     .Where(e => IsSequentialOrParallel(e)
                                 && string.Equals(e.ToNodeId, toNodeId, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(e => e.Id, StringComparer.Ordinal))
        {
            var from = e.FromNodeId.Trim();
            if (!store.TryGetValue(from, out var text) || string.IsNullOrWhiteSpace(text))
                continue;

            map.TryGetValue(from, out var node);
            var personaId = TryGetPersonaId(node?.Config);
            var label = string.IsNullOrWhiteSpace(node?.Label)
                ? (personaId ?? from)
                : node!.Label!.Trim();
            list.Add(new Section(personaId, label, text.Trim()));
        }

        return list;
    }

    private static string ComposeRanked(IReadOnlyList<Section> sections)
    {
        foreach (var pid in RankedPersonaPriority)
        {
            var hit = sections.FirstOrDefault(s =>
                string.Equals(s.PersonaId, pid, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(s.Text));
            if (hit != null)
                return hit.Text;
        }

        return sections.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Text))?.Text ?? "";
    }

    private static string ComposeMergeSections(IReadOnlyList<Section> sections)
    {
        if (sections.Count == 1)
            return sections[0].Text;

        // User-facing answers last so the reply reads naturally after ingest notes.
        var ordered = sections
            .OrderBy(s => RankIndex(s.PersonaId))
            .ThenBy(s => s.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sb = new StringBuilder();
        foreach (var s in ordered)
        {
            if (sb.Length > 0)
                sb.AppendLine().AppendLine();
            sb.Append("**").Append(s.Label).Append("**").AppendLine().AppendLine().Append(s.Text);
        }

        return sb.ToString();
    }

    private static int RankIndex(string? personaId)
    {
        if (string.IsNullOrWhiteSpace(personaId))
            return RankedPersonaPriority.Length;
        for (var i = 0; i < RankedPersonaPriority.Length; i++)
        {
            if (string.Equals(RankedPersonaPriority[i], personaId, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return RankedPersonaPriority.Length - 1;
    }

    private static string NormalizePolicy(string? policy)
    {
        var p = (policy ?? "merge_sections").Trim().ToLowerInvariant();
        return p is "first_non_empty" or "ranked" or "merge_sections" ? p : "merge_sections";
    }

    private static bool IsSequentialOrParallel(ScenarioFlowEdge e) =>
        string.IsNullOrWhiteSpace(e.Mode)
        || string.Equals(e.Mode, "sequential", StringComparison.OrdinalIgnoreCase)
        || string.Equals(e.Mode, "parallel", StringComparison.OrdinalIgnoreCase);

    private static string? TryGetPersonaId(JsonElement? config)
    {
        if (config is not { ValueKind: JsonValueKind.Object } el)
            return null;
        return el.TryGetProperty("personaId", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()?.Trim()
            : null;
    }
}
