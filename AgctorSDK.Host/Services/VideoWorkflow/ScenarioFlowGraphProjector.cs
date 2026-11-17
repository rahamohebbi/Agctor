using System.Text.Json;
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

    /// <summary>Stable mailbox / capability id used in run activity and live layouts.</summary>
    public static string CanonicalId(ScenarioFlowNode node)
    {
        if (node.Type.Equals("ChatInput", StringComparison.OrdinalIgnoreCase))
            return "Human";
        if (node.Type.Equals("Output", StringComparison.OrdinalIgnoreCase))
            return "Output";
        if (SceneVideoScenario.IsCapability(node))
            return string.IsNullOrWhiteSpace(node.Label) ? node.Id : node.Label.Trim();
        var mailbox = SceneVideoScenario.MailboxFor(node);
        if (mailbox != null) return mailbox;
        return string.IsNullOrWhiteSpace(node.Label) ? node.Id : node.Label.Trim();
    }

    public static string KindFor(ScenarioFlowNode node)
    {
        if (node.Type.Equals("ChatInput", StringComparison.OrdinalIgnoreCase)) return "human";
        // Distinct from Capability tools so live UIs do not treat Output as "Cynelo API".
        if (node.Type.Equals("Output", StringComparison.OrdinalIgnoreCase)) return "output";
        if (node.Type.Equals("LlmNode", StringComparison.OrdinalIgnoreCase)) return "llm";
        if (SceneVideoScenario.IsCapability(node) &&
            node.Config is { ValueKind: JsonValueKind.Object } cfg &&
            cfg.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String)
            return kind.GetString() ?? "tool";
        if (node.Type.Equals("AgentLoop", StringComparison.OrdinalIgnoreCase)) return "agent";
        return "agent";
    }

    /// <summary>Card title on the live graph — flow-editor label (not shared tool blurbs).</summary>
    public static string DisplayDetail(ScenarioFlowNode node)
    {
        if (!string.IsNullOrWhiteSpace(node.Label)) return node.Label.Trim();
        if (SceneVideoScenario.IsCapability(node) &&
            node.Config is { ValueKind: JsonValueKind.Object } cfg &&
            cfg.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(detail.GetString()))
            return detail.GetString()!;
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
            // Detail carries the flow-editor label / capability detail for readable titles.
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
    /// Prefer exact / alias matches on the expanded roster before collapsing to Director.
    /// </summary>
    public static string MapOntoFlow(string endpoint, IReadOnlySet<string> flowIds)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return endpoint;
        if (flowIds.Contains(endpoint)) return endpoint;

        foreach (var (alias, canon) in Aliases)
        {
            if (!endpoint.Equals(alias, StringComparison.OrdinalIgnoreCase) &&
                !endpoint.StartsWith(alias + " · ", StringComparison.OrdinalIgnoreCase) &&
                !endpoint.StartsWith(alias + " ·", StringComparison.OrdinalIgnoreCase))
                continue;
            if (flowIds.Contains(canon)) return canon;
        }

        if (endpoint.StartsWith("Ollama", StringComparison.OrdinalIgnoreCase) && flowIds.Contains("Ollama"))
            return "Ollama";

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

        if (flowIds.Contains("Video rendering") &&
            (endpoint.Contains("Video rendering", StringComparison.OrdinalIgnoreCase) ||
             endpoint.Contains("ComfyUI", StringComparison.OrdinalIgnoreCase)))
            return "Video rendering";

        if (flowIds.Contains("Render agent") &&
            endpoint.Contains("Render", StringComparison.OrdinalIgnoreCase))
            return "Render agent";

        if (flowIds.Contains("Director"))
            return "Director";

        return flowIds.FirstOrDefault(id =>
                   !id.Equals("Human", StringComparison.Ordinal) &&
                   !id.Equals("Output", StringComparison.Ordinal))
               ?? flowIds.First();
    }

    static readonly (string Alias, string Canon)[] Aliases =
    [
        ("Continuity critic", SceneVideoRoles.VisualCritic),
        ("Visual critic", SceneVideoRoles.VisualCritic),
        ("Memory agent", SceneVideoRoles.Memory),
        ("Scene Memory agent", SceneVideoRoles.Memory),
        ("Render agent", SceneVideoRoles.Render),
        ("Render", SceneVideoRoles.Render),
        ("Project agent", SceneVideoRoles.Project),
        ("Project", SceneVideoRoles.Project),
        ("Editor agent", SceneVideoRoles.Editor),
        ("Editor", SceneVideoRoles.Editor),
        ("Planner", SceneVideoRoles.Planner),
        ("Audio Engineer", SceneVideoRoles.AudioEngineer),
        ("Director", SceneVideoRoles.Director),
        ("Video rendering", "Video rendering"),
        ("Frame sampler", "Frame sampler"),
        ("Audio production", "Audio production"),
        ("Speech inspection", "Speech inspection"),
        ("Project storage", "Project storage"),
        ("Story storage", "Story storage"),
        ("Film assembly", "Film assembly"),
        ("Scene Memory", "Scene Memory"),
        ("ComfyUI", "Video rendering")
    ];

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
