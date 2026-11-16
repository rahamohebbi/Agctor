using System.Text.Json;
using AgctorSDK.Core.Workflows;
using AgctorSDK.Host.Services.Scenarios;

namespace AgctorSDK.Host.Services.VideoWorkflow;

/// <summary>
/// Projects the scenario conversation-flow document into the live agent graph skeleton.
/// Flow is the single definition surface; observed routes union on top.
/// </summary>
public static class ScenarioFlowGraphProjector
{
    public sealed record Projection(
        AgentRunGraph Skeleton,
        IReadOnlyDictionary<string, (double X, double Y)> Layouts,
        ScenarioFlowDocument Flow,
        IReadOnlyDictionary<string, string> FlowIdToCanonical);

    /// <summary>Map a flow node id to the mailbox / route id used in run activity.</summary>
    public static string CanonicalId(ScenarioFlowNode node)
    {
        if (node.Type.Equals("ChatInput", StringComparison.OrdinalIgnoreCase))
            return "Human";
        if (SceneVideoScenario.IsAudio(node))
            return "Audio Engineer";
        if (SceneVideoScenario.IsLoop(node))
            return "Director";
        if (node.Type.Equals("Output", StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrWhiteSpace(node.Label) ? "Output" : node.Label.Trim();
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
            var detail = node.Type.Equals("AgentLoop", StringComparison.OrdinalIgnoreCase)
                ? "Actor mailbox"
                : (string.IsNullOrWhiteSpace(node.Label) ? node.Type : node.Label);
            nodes[id] = new AgentGraphNode(id, KindFor(node), detail);
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

    /// <summary>Union flow skeleton with observed activity (specialists/tools appear when recorded).</summary>
    public static AgentRunGraph Compose(Projection definition, AgentRunGraph observed)
    {
        var nodes = new Dictionary<string, AgentGraphNode>(StringComparer.Ordinal);
        foreach (var n in definition.Skeleton.Nodes)
            nodes[n.Id] = n;

        foreach (var n in observed.Nodes)
        {
            if (nodes.TryGetValue(n.Id, out var existing))
                nodes[n.Id] = new AgentGraphNode(n.Id, existing.Kind, n.Detail ?? existing.Detail);
            else
                nodes[n.Id] = n;
        }

        var edges = new Dictionary<string, AgentGraphEdge>(StringComparer.Ordinal);
        foreach (var e in definition.Skeleton.Edges)
            edges[e.From + "\0" + e.To] = e;
        foreach (var e in observed.Edges)
            edges[e.From + "\0" + e.To] = e;

        return new AgentRunGraph(
            nodes.Values.OrderBy(n => n.Id, StringComparer.Ordinal).ToArray(),
            edges.Values.ToArray(),
            observed.ActiveCalls);
    }

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
