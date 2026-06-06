using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgctorSDK.Host.Services.Scenarios;

/// <summary>
/// Domain validation for <see cref="ScenarioFlowDocument"/> (structure + persona refs). PRD-014.
/// </summary>
public static class ScenarioFlowValidator
{
    private static readonly HashSet<string> NodeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ChatInput", "Router", "LlmNode", "Merge", "Output",
        "Gate", "WaitForInput", "AwaitEvent", "Notify"
    };

    private static readonly HashSet<string> EdgeModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "sequential", "parallel", "loopBack"
    };

    private static readonly HashSet<string> StoreInvalidationPolicies = new(StringComparer.OrdinalIgnoreCase)
    {
        "fromTargetForward", "keepAll", "iterationScopeOnly"
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

            if (string.Equals(n.Type, "LlmNode", StringComparison.OrdinalIgnoreCase))
            {
                var pid = TryGetPersonaId(n.Config);
                if (string.IsNullOrWhiteSpace(pid))
                    errors.Add($"Scenario '{scenario.Id}' flow: LlmNode node '{n.Id}' requires config.personaId.");
                else if (!personaRoster.Contains(pid, StringComparer.OrdinalIgnoreCase))
                    errors.Add($"Scenario '{scenario.Id}' flow: LlmNode '{n.Id}' references personaId '{pid}' not listed in personaAgentIds.");
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

            if (string.Equals(e.Mode, "loopBack", StringComparison.OrdinalIgnoreCase))
            {
                if (e.LoopConfig == null)
                    errors.Add($"Scenario '{scenario.Id}' flow: loopBack edge '{e.Id}' requires loopConfig.");
                else
                {
                    if (string.IsNullOrWhiteSpace(e.LoopConfig.LoopRegionId))
                        errors.Add($"Scenario '{scenario.Id}' flow: loopBack edge '{e.Id}' requires loopConfig.loopRegionId.");
                    if (e.LoopConfig.MaxAttempts < 1)
                        errors.Add($"Scenario '{scenario.Id}' flow: loopBack edge '{e.Id}' requires loopConfig.maxAttempts >= 1.");
                    if (!StoreInvalidationPolicies.Contains(e.LoopConfig.StoreInvalidation ?? string.Empty))
                        errors.Add($"Scenario '{scenario.Id}' flow: loopBack edge '{e.Id}' has invalid storeInvalidation.");
                }
            }
        }

        ValidateV2Nodes(scenario, flow, edgeList, errors);
        foreach (var n in flow.Nodes)
        {
            if (!string.Equals(n.Type, "Router", StringComparison.OrdinalIgnoreCase))
                continue;
            var rcfg = ScenarioFlowRouterConfig.Parse(n.Config);
            if (rcfg.Mode == ScenarioFlowRouterMode.Llm)
                continue;

            var rId = n.Id.Trim();
            var seqEdges = edgeList
                .Where(e => string.Equals(e.FromNodeId, rId, StringComparison.OrdinalIgnoreCase)
                            && (string.IsNullOrWhiteSpace(e.Mode)
                                || string.Equals(e.Mode, "sequential", StringComparison.OrdinalIgnoreCase)))
                .OrderBy(e => e.Id, StringComparer.Ordinal)
                .ToList();
            var defaultCount = seqEdges.Count(e => string.IsNullOrWhiteSpace(e.Condition));
            if (defaultCount > 1)
            {
                errors.Add(
                    $"Scenario '{scenario.Id}' flow: Router '{rId}' (deterministic) has {defaultCount} default (empty condition) sequential edges; keep at most one.");
            }

            foreach (var e in seqEdges)
            {
                if (string.IsNullOrWhiteSpace(e.Condition))
                    continue;
                if (!string.Equals(e.ConditionMatch?.Trim(), "regex", StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    _ = new Regex(e.Condition.Trim(), RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
                }
                catch (ArgumentException)
                {
                    errors.Add(
                        $"Scenario '{scenario.Id}' flow: edge '{e.Id}' from Router '{rId}' has invalid regex in condition.");
                }
            }
        }

        // Phase 10: LLM Router — sequential edges only to LlmNode; shared Merge when multiple branches may run in parallel (see maxTargets==1 exception below).
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
                errors.Add($"Scenario '{scenario.Id}' flow: Router '{rId}' (llm) needs sequential edges to LlmNode nodes.");
                continue;
            }

            var personaBranchIds = new List<string>();
            foreach (var e in seqEdges)
            {
                var targetId = e.ToNodeId.Trim();
                var tn = flow.Nodes.FirstOrDefault(x =>
                    string.Equals(x.Id.Trim(), targetId, StringComparison.OrdinalIgnoreCase));
                if (tn == null || !string.Equals(tn.Type, "LlmNode", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(
                        $"Scenario '{scenario.Id}' flow: Router '{rId}' (llm) sequential edge '{e.Id}' must target a LlmNode node.");
                    continue;
                }

                var pid = TryGetPersonaId(tn.Config);
                if (string.IsNullOrWhiteSpace(pid))
                    errors.Add($"Scenario '{scenario.Id}' flow: Router '{rId}' target LlmNode '{tn.Id}' requires config.personaId.");

                personaBranchIds.Add(targetId);
            }

            if (personaBranchIds.Count >= 2)
            {
                // Multiple LlmNode candidates can only run in parallel when the LLM returns >1 target.
                // maxTargets == 1 caps the parser to one persona, so each message uses a single linear branch
                // (no shared Merge required — same as one candidate at design time).
                var singleTargetCap = rcfg.TargetPolicy == ScenarioFlowRouterTargetPolicy.SingleBest
                                      || rcfg.EffectiveMaxTargets == 1;
                if (!singleTargetCap)
                {
                    var merge = ScenarioFlowGraphInterpreter.FindCommonMergeForBranchStarts(flow, personaBranchIds);
                    if (merge == null)
                    {
                        errors.Add(
                            $"Scenario '{scenario.Id}' flow: Router '{rId}' (llm) has multiple LlmNode branches but no shared Merge before Output. Add one Merge where every branch meets, or set routerTargetPolicy to single_best (or maxTargets to 1) so at most one branch runs per message.");
                    }
                }
            }
            else if (personaBranchIds.Count == 1 && !NodeCanReachOutput(flow, edgeList, personaBranchIds[0]))
            {
                errors.Add($"Scenario '{scenario.Id}' flow: Router '{rId}' (llm) LlmNode branch must reach an Output node.");
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
                        $"Scenario '{scenario.Id}' flow: Router '{rId}' fallbackPersonaId must match a LlmNode candidate personaId.");
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

    /// <summary>PRD-024: Gate branches, suspend resume paths, loop region consistency.</summary>
    private static void ValidateV2Nodes(
        ScenarioDefinition scenario,
        ScenarioFlowDocument flow,
        List<ScenarioFlowEdge> edgeList,
        List<string> errors)
    {
        var edgeIds = new HashSet<string>(
            edgeList.Where(e => !string.IsNullOrWhiteSpace(e.Id)).Select(e => e.Id.Trim()),
            StringComparer.OrdinalIgnoreCase);

        var regionAttempts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in edgeList.Where(e => string.Equals(e.Mode, "loopBack", StringComparison.OrdinalIgnoreCase)))
        {
            var lc = e.LoopConfig;
            if (lc == null || string.IsNullOrWhiteSpace(lc.LoopRegionId))
                continue;
            if (regionAttempts.TryGetValue(lc.LoopRegionId, out var existing) && existing != lc.MaxAttempts)
            {
                errors.Add(
                    $"Scenario '{scenario.Id}' flow: loop region '{lc.LoopRegionId}' has conflicting maxAttempts across loopBack edges.");
            }
            else
            {
                regionAttempts[lc.LoopRegionId] = lc.MaxAttempts;
            }
        }

        foreach (var n in flow.Nodes)
        {
            if (string.Equals(n.Type, "Gate", StringComparison.OrdinalIgnoreCase))
            {
                if (n.Config is not { ValueKind: JsonValueKind.Object } cfg)
                {
                    errors.Add($"Scenario '{scenario.Id}' flow: Gate '{n.Id}' requires config.");
                    continue;
                }

                var fact = cfg.TryGetProperty("fact", out var f) ? f.GetString()?.Trim() : null;
                if (string.IsNullOrEmpty(fact))
                    errors.Add($"Scenario '{scenario.Id}' flow: Gate '{n.Id}' requires config.fact.");

                var trueEdge = cfg.TryGetProperty("trueEdgeId", out var te) ? te.GetString()?.Trim() : null;
                var falseEdge = cfg.TryGetProperty("falseEdgeId", out var fe) ? fe.GetString()?.Trim() : null;
                if (string.IsNullOrEmpty(trueEdge) || string.IsNullOrEmpty(falseEdge))
                {
                    errors.Add($"Scenario '{scenario.Id}' flow: Gate '{n.Id}' requires trueEdgeId and falseEdgeId.");
                }
                else
                {
                    if (!edgeIds.Contains(trueEdge))
                        errors.Add($"Scenario '{scenario.Id}' flow: Gate '{n.Id}' trueEdgeId '{trueEdge}' not found.");
                    if (!edgeIds.Contains(falseEdge))
                        errors.Add($"Scenario '{scenario.Id}' flow: Gate '{n.Id}' falseEdgeId '{falseEdge}' not found.");
                }
            }

            if (string.Equals(n.Type, "WaitForInput", StringComparison.OrdinalIgnoreCase))
            {
                var prompt = TryGetStringConfig(n.Config, "promptTemplate");
                if (string.IsNullOrWhiteSpace(prompt))
                    errors.Add($"Scenario '{scenario.Id}' flow: WaitForInput '{n.Id}' requires config.promptTemplate.");

                var hasResume = edgeList.Any(e =>
                    string.Equals(e.FromNodeId, n.Id, StringComparison.OrdinalIgnoreCase)
                    && (string.Equals(e.Mode, "loopBack", StringComparison.OrdinalIgnoreCase)
                        || string.IsNullOrWhiteSpace(e.Mode)
                        || string.Equals(e.Mode, "sequential", StringComparison.OrdinalIgnoreCase)));
                if (!hasResume)
                {
                    errors.Add(
                        $"Scenario '{scenario.Id}' flow: WaitForInput '{n.Id}' needs a loopBack or sequential outgoing edge.");
                }
            }

            if (string.Equals(n.Type, "AwaitEvent", StringComparison.OrdinalIgnoreCase))
            {
                var eventType = TryGetStringConfig(n.Config, "eventType");
                if (string.IsNullOrWhiteSpace(eventType))
                    errors.Add($"Scenario '{scenario.Id}' flow: AwaitEvent '{n.Id}' requires config.eventType.");
            }

            if (string.Equals(n.Type, "Notify", StringComparison.OrdinalIgnoreCase))
            {
                var hasOut = edgeList.Any(e =>
                    string.Equals(e.FromNodeId, n.Id, StringComparison.OrdinalIgnoreCase)
                    && (string.IsNullOrWhiteSpace(e.Mode)
                        || string.Equals(e.Mode, "sequential", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(e.Mode, "loopBack", StringComparison.OrdinalIgnoreCase)));
                if (!hasOut)
                    errors.Add($"Scenario '{scenario.Id}' flow: Notify '{n.Id}' requires an outgoing edge.");
            }
        }
    }

    private static string? TryGetStringConfig(JsonElement? config, string key)
    {
        if (config is not { ValueKind: JsonValueKind.Object } el) return null;
        return el.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
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
