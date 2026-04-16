using System.Text.Json;

namespace AgctorSDK.Host.Services.Scenarios;

/// <summary>
/// Domain validation for <see cref="ScenarioFlowDocument"/> (structure + persona refs). PRD-014.
/// </summary>
public static class ScenarioFlowValidator
{
    private static readonly HashSet<string> NodeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ChatInput", "Router", "PersonaCall", "Merge", "Output"
    };

    private static readonly HashSet<string> EdgeModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "sequential", "parallel"
    };

    private static readonly HashSet<string> OutputPolicies = new(StringComparer.OrdinalIgnoreCase)
    {
        "first_non_empty", "merge_sections", "ranked"
    };

    public static IReadOnlyList<string> Validate(ScenarioDefinition scenario)
    {
        var errors = new List<string>();
        var flow = scenario.Flow;
        if (flow == null) return errors;

        if (string.IsNullOrWhiteSpace(flow.SchemaVersion))
            errors.Add($"Scenario '{scenario.Id}' flow: schemaVersion is required.");
        if (string.IsNullOrWhiteSpace(flow.GraphId))
            errors.Add($"Scenario '{scenario.Id}' flow: graphId is required.");

        if (!OutputPolicies.Contains(flow.OutputPolicy ?? string.Empty))
            errors.Add($"Scenario '{scenario.Id}' flow: invalid outputPolicy '{flow.OutputPolicy}'.");

        if (flow.Nodes == null || flow.Nodes.Count == 0)
        {
            errors.Add($"Scenario '{scenario.Id}' flow: nodes must not be empty.");
            return errors;
        }

        var edgeList = flow.Edges ?? new List<ScenarioFlowEdge>();
        if (flow.Edges == null)
            errors.Add($"Scenario '{scenario.Id}' flow: edges is required (may be empty array).");

        var personaRoster = scenario.PersonaAgentIds ?? new List<string>();
        var nodeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in flow.Nodes)
        {
            if (string.IsNullOrWhiteSpace(n.Id))
            {
                errors.Add($"Scenario '{scenario.Id}' flow: node missing id.");
                continue;
            }

            if (!nodeIds.Add(n.Id.Trim()))
                errors.Add($"Scenario '{scenario.Id}' flow: duplicate node id '{n.Id}'.");

            if (string.IsNullOrWhiteSpace(n.Type) || !NodeTypes.Contains(n.Type))
                errors.Add($"Scenario '{scenario.Id}' flow: node '{n.Id}' has invalid type '{n.Type}'.");

            if (string.Equals(n.Type, "PersonaCall", StringComparison.OrdinalIgnoreCase))
            {
                var pid = TryGetPersonaId(n.Config);
                if (string.IsNullOrWhiteSpace(pid))
                    errors.Add($"Scenario '{scenario.Id}' flow: PersonaCall node '{n.Id}' requires config.personaId.");
                else if (!personaRoster.Contains(pid, StringComparer.OrdinalIgnoreCase))
                    errors.Add($"Scenario '{scenario.Id}' flow: PersonaCall '{n.Id}' references personaId '{pid}' not listed in personaAgentIds.");
            }
        }

        var inputs = flow.Nodes.Count(n => string.Equals(n.Type, "ChatInput", StringComparison.OrdinalIgnoreCase));
        var outputs = flow.Nodes.Count(n => string.Equals(n.Type, "Output", StringComparison.OrdinalIgnoreCase));
        if (inputs < 1)
            errors.Add($"Scenario '{scenario.Id}' flow: requires at least one ChatInput node.");
        if (outputs < 1)
            errors.Add($"Scenario '{scenario.Id}' flow: requires at least one Output node.");

        foreach (var e in edgeList)
        {
            if (string.IsNullOrWhiteSpace(e.Id))
                errors.Add($"Scenario '{scenario.Id}' flow: edge missing id.");
            if (string.IsNullOrWhiteSpace(e.FromNodeId) || !nodeIds.Contains(e.FromNodeId))
                errors.Add($"Scenario '{scenario.Id}' flow: edge '{e.Id}' has unknown fromNodeId '{e.FromNodeId}'.");
            if (string.IsNullOrWhiteSpace(e.ToNodeId) || !nodeIds.Contains(e.ToNodeId))
                errors.Add($"Scenario '{scenario.Id}' flow: edge '{e.Id}' has unknown toNodeId '{e.ToNodeId}'.");
            if (string.IsNullOrWhiteSpace(e.Mode) || !EdgeModes.Contains(e.Mode))
                errors.Add($"Scenario '{scenario.Id}' flow: edge '{e.Id}' has invalid mode '{e.Mode}'.");
        }

        // Phase 10: LLM Router — sequential edges only to PersonaCall; shared Merge when multiple branches.
        foreach (var n in flow.Nodes)
        {
            if (!string.Equals(n.Type, "Router", StringComparison.OrdinalIgnoreCase))
                continue;
            var rcfg = ScenarioFlowRouterConfig.Parse(n.Config);
            if (rcfg.Mode != ScenarioFlowRouterMode.Llm)
                continue;

            var rId = n.Id.Trim();
            var parFromR = edgeList.Count(e =>
                string.Equals(e.FromNodeId, rId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(e.Mode, "parallel", StringComparison.OrdinalIgnoreCase));
            if (parFromR > 0)
                errors.Add($"Scenario '{scenario.Id}' flow: Router '{rId}' with routerMode llm cannot have parallel outgoing edges.");

            var seqEdges = edgeList
                .Where(e => string.Equals(e.FromNodeId, rId, StringComparison.OrdinalIgnoreCase)
                            && (string.IsNullOrWhiteSpace(e.Mode)
                                || string.Equals(e.Mode, "sequential", StringComparison.OrdinalIgnoreCase)))
                .OrderBy(e => e.Id, StringComparer.Ordinal)
                .ToList();
            if (seqEdges.Count == 0)
            {
                errors.Add($"Scenario '{scenario.Id}' flow: Router '{rId}' (llm) needs sequential edges to PersonaCall nodes.");
                continue;
            }

            var personaBranchIds = new List<string>();
            foreach (var e in seqEdges)
            {
                var targetId = e.ToNodeId.Trim();
                var tn = flow.Nodes.FirstOrDefault(x =>
                    string.Equals(x.Id.Trim(), targetId, StringComparison.OrdinalIgnoreCase));
                if (tn == null || !string.Equals(tn.Type, "PersonaCall", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(
                        $"Scenario '{scenario.Id}' flow: Router '{rId}' (llm) sequential edge '{e.Id}' must target a PersonaCall node.");
                    continue;
                }

                var pid = TryGetPersonaId(tn.Config);
                if (string.IsNullOrWhiteSpace(pid))
                    errors.Add($"Scenario '{scenario.Id}' flow: Router '{rId}' target PersonaCall '{tn.Id}' requires config.personaId.");

                personaBranchIds.Add(targetId);
            }

            if (personaBranchIds.Count >= 2)
            {
                var merge = ScenarioFlowGraphInterpreter.FindCommonMergeForBranchStarts(flow, personaBranchIds);
                if (merge == null)
                {
                    errors.Add(
                        $"Scenario '{scenario.Id}' flow: Router '{rId}' (llm) PersonaCall branches must reach exactly one shared Merge.");
                }
            }
            else if (personaBranchIds.Count == 1 && !NodeCanReachOutput(flow, edgeList, personaBranchIds[0]))
            {
                errors.Add($"Scenario '{scenario.Id}' flow: Router '{rId}' (llm) PersonaCall branch must reach an Output node.");
            }

            if (!string.IsNullOrWhiteSpace(rcfg.FallbackPersonaId))
            {
                if (!personaRoster.Contains(rcfg.FallbackPersonaId, StringComparer.OrdinalIgnoreCase))
                {
                    errors.Add(
                        $"Scenario '{scenario.Id}' flow: Router '{rId}' fallbackPersonaId '{rcfg.FallbackPersonaId}' is not listed in personaAgentIds.");
                }

                var cand = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var bid in personaBranchIds)
                {
                    var tn = flow.Nodes.FirstOrDefault(x =>
                        string.Equals(x.Id.Trim(), bid, StringComparison.OrdinalIgnoreCase));
                    var p = tn != null ? TryGetPersonaId(tn.Config) : null;
                    if (!string.IsNullOrWhiteSpace(p))
                        cand.Add(p!);
                }

                if (!cand.Contains(rcfg.FallbackPersonaId))
                {
                    errors.Add(
                        $"Scenario '{scenario.Id}' flow: Router '{rId}' fallbackPersonaId must match a PersonaCall candidate personaId.");
                }
            }

            if (rcfg.MaxTargets is { } mt && mt < 1)
                errors.Add($"Scenario '{scenario.Id}' flow: Router '{rId}' maxTargets must be at least 1 when set.");
        }

        // Reachability: some ChatInput can reach some Output following directed edges.
        if (inputs >= 1 && outputs >= 1 && edgeList.Count > 0)
        {
            var adj = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in edgeList)
            {
                if (string.IsNullOrWhiteSpace(e.FromNodeId) || string.IsNullOrWhiteSpace(e.ToNodeId)) continue;
                if (!adj.TryGetValue(e.FromNodeId, out var list))
                {
                    list = new List<string>();
                    adj[e.FromNodeId] = list;
                }

                list.Add(e.ToNodeId);
            }

            var startNodes = flow.Nodes
                .Where(n => string.Equals(n.Type, "ChatInput", StringComparison.OrdinalIgnoreCase))
                .Select(n => n.Id)
                .ToList();
            var goalNodes = new HashSet<string>(
                flow.Nodes.Where(n => string.Equals(n.Type, "Output", StringComparison.OrdinalIgnoreCase)).Select(n => n.Id),
                StringComparer.OrdinalIgnoreCase);

            var anyReachable = false;
            foreach (var start in startNodes)
            {
                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var q = new Queue<string>();
                q.Enqueue(start);
                visited.Add(start);
                while (q.Count > 0)
                {
                    var u = q.Dequeue();
                    if (goalNodes.Contains(u))
                    {
                        anyReachable = true;
                        break;
                    }

                    if (!adj.TryGetValue(u, out var outs)) continue;
                    foreach (var v in outs)
                    {
                        if (visited.Add(v)) q.Enqueue(v);
                    }
                }

                if (anyReachable) break;
            }

            if (!anyReachable)
                errors.Add($"Scenario '{scenario.Id}' flow: no path from ChatInput to Output.");
        }

        return errors;
    }

    private static bool NodeCanReachOutput(ScenarioFlowDocument flow, List<ScenarioFlowEdge> edges, string start)
    {
        var goals = new HashSet<string>(
            (flow.Nodes ?? new List<ScenarioFlowNode>())
            .Where(n => string.Equals(n.Type, "Output", StringComparison.OrdinalIgnoreCase))
            .Select(n => n.Id.Trim()),
            StringComparer.OrdinalIgnoreCase);
        var adj = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in edges)
        {
            if (string.IsNullOrWhiteSpace(e.FromNodeId) || string.IsNullOrWhiteSpace(e.ToNodeId))
                continue;
            var a = e.FromNodeId.Trim();
            var b = e.ToNodeId.Trim();
            if (!adj.TryGetValue(a, out var list))
            {
                list = new List<string>();
                adj[a] = list;
            }

            list.Add(b);
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var q = new Queue<string>();
        var s0 = start.Trim();
        q.Enqueue(s0);
        seen.Add(s0);
        while (q.Count > 0)
        {
            var u = q.Dequeue();
            if (goals.Contains(u))
                return true;
            if (!adj.TryGetValue(u, out var outs))
                continue;
            foreach (var v in outs)
            {
                if (seen.Add(v))
                    q.Enqueue(v);
            }
        }

        return false;
    }

    private static string? TryGetPersonaId(JsonElement? config)
    {
        if (config is not { ValueKind: JsonValueKind.Object } el) return null;
        return el.TryGetProperty("personaId", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;
    }
}
