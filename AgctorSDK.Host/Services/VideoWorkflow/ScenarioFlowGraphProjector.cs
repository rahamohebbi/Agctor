using AgctorSDK.Core.Workflows;
using AgctorSDK.Host.Services.Scenarios;

namespace AgctorSDK.Host.Services.VideoWorkflow;

/// <summary>
/// Projects the scenario conversation-flow document into the live agent graph skeleton.
/// Flow is the only product picture; observed activity remaps onto flow nodes (never a second topology).
/// </summary>
public static class ScenarioFlowGraphProjector
{
    public sealed record Projection(
        AgentRunGraph Skeleton,
        IReadOnlyDictionary<string, (double X, double Y)> Layouts,
        ScenarioFlowDocument Flow,
        IReadOnlyDictionary<string, string> FlowIdToCanonical);

    /// <summary>Stable mailbox / route id used in run activity and live layouts.</summary>
    public static string CanonicalId(ScenarioFlowNode node)
    {
        if (node.Type.Equals("ChatInput", StringComparison.OrdinalIgnoreCase))
            return "Human";
        if (SceneVideoScenario.IsAudio(node))
            return "Audio Engineer";
        if (SceneVideoScenario.IsLoop(node))
            return "Director";
        // Keep Output id short so layouts and remapping stay stable across label edits.
        if (node.Type.Equals("Output", StringComparison.OrdinalIgnoreCase))
            return "Output";
        return string.IsNullOrWhiteSpace(node.Label) ? node.Id : node.Label.Trim();
    }

    public static string KindFor(ScenarioFlowNode node)
    {
        if (node.Type.Equals("ChatInput", StringComparison.OrdinalIgnoreCase)) return "human";
        if (node.Type.Equals("Output", StringComparison.OrdinalIgnoreCase)) return "tool";
        if (node.Type.Equals("LlmNode", StringComparison.OrdinalIgnoreCase)) return "llm";
        if (node.Type.Equals("AgentLoop", StringComparison.OrdinalIgnoreCase)) return "agent";
        return "agent";
    }

    /// <summary>Display title on the live graph — prefer the saved flow label.</summary>
    public static string DisplayDetail(ScenarioFlowNode node)
    {
        if (!string.IsNullOrWhiteSpace(node.Label)) return node.Label.Trim();
        return node.Type;
    }

    public static Projection? Project(ScenarioFlowDocument? flow)
    {
        if (flow?.Nodes == null || flow.Nodes.Count == 0) return null;

        var flowToCanon = new Dictionary<string, string>(StringComparer.Ordinal);
        var nodes = new Dictionary<string, AgentGraphNode>(StringComparer.Ordinal);
        foreach (var node in flow.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id)) continue;
            var id = CanonicalId(node);
            flowToCanon[node.Id] = id;
            // Detail carries the flow-editor label so clients can show "Your idea", not just "Human".
            nodes[id] = new AgentGraphNode(id, KindFor(node), DisplayDetail(node));
        }

        var edges = new List<AgentGraphEdge>();
        foreach (var edge in flow.Edges ?? [])
        {
            if (!flowToCanon.TryGetValue(edge.FromNodeId, out var from)) continue;
            if (!flowToCanon.TryGetValue(edge.ToNodeId, out var to)) continue;
            edges.Add(new AgentGraphEdge(from, to, edge.Mode ?? "sequential", "defined", 0, DateTimeOffset.MinValue));
        }

        var layouts = new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal);
        if (flow.Ui?.NodeLayouts != null)
        {
            foreach (var (flowId, layout) in flow.Ui.NodeLayouts)
            {
                if (!flowToCanon.TryGetValue(flowId, out var canon)) continue;
                layouts[canon] = (layout.X, layout.Y);
            }
        }

        return new Projection(
            new AgentRunGraph(nodes.Values.OrderBy(n => n.Id, StringComparer.Ordinal).ToArray(), edges.ToArray()),
            layouts,
            flow,
            flowToCanon);
    }

    /// <summary>
    /// Map an observed actor/tool/llm id onto the nearest conversation-flow node.
    /// Specialists (Render, critics, ComfyUI, Ollama, …) collapse onto Director unless clearly audio.
    /// </summary>
    public static string MapOntoFlow(string endpoint, IReadOnlySet<string> flowIds)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return endpoint;
        if (flowIds.Contains(endpoint)) return endpoint;

        if (flowIds.Contains("Output") &&
            (endpoint.Equals("Output", StringComparison.OrdinalIgnoreCase) ||
             endpoint.Contains("Finished", StringComparison.OrdinalIgnoreCase)))
            return "Output";

        if (flowIds.Contains("Human") &&
            (endpoint.Equals("Human", StringComparison.OrdinalIgnoreCase) ||
             endpoint.Equals("You", StringComparison.OrdinalIgnoreCase)))
            return "Human";

        if (flowIds.Contains("Audio Engineer") && LooksLikeAudio(endpoint))
            return "Audio Engineer";

        if (flowIds.Contains("Director"))
            return "Director";

        return flowIds.FirstOrDefault(id =>
                   !id.Equals("Human", StringComparison.Ordinal) &&
                   !id.Equals("Output", StringComparison.Ordinal))
               ?? flowIds.First();
    }

    static bool LooksLikeAudio(string endpoint) =>
        endpoint.Contains("Audio", StringComparison.OrdinalIgnoreCase) ||
        endpoint.Contains("Speech", StringComparison.OrdinalIgnoreCase) ||
        endpoint.Contains("Narrat", StringComparison.OrdinalIgnoreCase) ||
        endpoint.Contains("Mix", StringComparison.OrdinalIgnoreCase) ||
        endpoint.Equals("RoFormer", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Flow-only compose: skeleton nodes/edges stay exactly as edited.
    /// Observed routes never add a second topology; activity remaps onto flow endpoints.
    /// </summary>
    public static AgentRunGraph Compose(Projection definition, AgentRunGraph observed)
    {
        var flowIds = definition.Skeleton.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        var nodes = definition.Skeleton.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);

        var edges = new Dictionary<string, AgentGraphEdge>(StringComparer.Ordinal);
        foreach (var e in definition.Skeleton.Edges)
            edges[Key(e.From, e.To)] = e;

        // Fold observed traffic onto existing flow edges (counts / last activity) without inventing edges.
        foreach (var e in observed.Edges)
        {
            var from = MapOntoFlow(e.From, flowIds);
            var to = MapOntoFlow(e.To, flowIds);
            if (from == to) continue;
            var key = Key(from, to);
            if (!edges.TryGetValue(key, out var existing)) continue;
            edges[key] = existing with
            {
                Count = existing.Count + Math.Max(1, e.Count),
                Outcome = e.Outcome ?? existing.Outcome,
                LastAt = e.LastAt > existing.LastAt ? e.LastAt : existing.LastAt,
                Operation = string.IsNullOrWhiteSpace(e.Operation) ? existing.Operation : e.Operation
            };
        }

        var active = (observed.ActiveCalls ?? Array.Empty<AgentActiveCall>())
            .Select(c => c with
            {
                From = MapOntoFlow(c.From, flowIds),
                To = MapOntoFlow(c.To, flowIds)
            })
            .ToArray();

        return new AgentRunGraph(
            nodes.Values.OrderBy(n => n.Id, StringComparer.Ordinal).ToArray(),
            edges.Values.ToArray(),
            active);
    }

    static string Key(string from, string to) => from + "\0" + to;

    /// <summary>Attach projected layouts onto a clone of the flow document for clients (canonical ids).</summary>
    public static ScenarioFlowDocument WithCanonicalLayouts(Projection projection)
    {
        var clone = ScenarioFlowDocument.Clone(projection.Flow)!;
        clone.Ui ??= new ScenarioFlowUi();
        clone.Ui.NodeLayouts = projection.Layouts.ToDictionary(
            kv => kv.Key,
            kv => new ScenarioFlowNodeLayout { X = kv.Value.X, Y = kv.Value.Y },
            StringComparer.Ordinal);
        return clone;
    }
}
