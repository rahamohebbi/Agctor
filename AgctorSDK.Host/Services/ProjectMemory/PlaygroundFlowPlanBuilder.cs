using System.Text.Json;
using AgctorSDK.Host.Services.Scenarios;

namespace AgctorSDK.Host.Services.ProjectMemory;

/// <summary>
/// Builds the Playground "Request pipeline" step list from a scenario's saved <see cref="ScenarioDefinition.Flow"/>
/// so the UI matches the Scenario flow designer. Falls back to a fixed template when there is no flow or no path.
/// </summary>
public static class PlaygroundFlowPlanBuilder
{
    public const string IngestStepId = "ingest";

    /// <summary>Stable id prefix for a Playground-only <c>LlmNode</c> chip when the selected agent is not on the graph path.</summary>
    public const string SyntheticLlmNodeStepIdPrefix = "pm-play-llmnode-";

    /// <summary>
    /// One chip in <c>flow_plan.steps</c>. <see cref="NodeKind"/> is used server-side for SSE sequencing; not sent to the client.
    /// </summary>
    public sealed class Step
    {
        public Step(string id, string label, bool optional, bool active, string nodeKind, string? personaId = null)
        {
            Id = id;
            Label = label;
            Optional = optional;
            Active = active;
            NodeKind = nodeKind;
            PersonaId = personaId;
        }

        public string Id { get; }
        public string Label { get; }
        public bool Optional { get; }
        public bool Active { get; }
        public string NodeKind { get; }
        public string? PersonaId { get; }
    }

    public sealed class Result
    {
        public Result(IReadOnlyList<Step> steps, bool fromScenarioGraph, bool usedSyntheticLlmNode)
        {
            Steps = steps;
            FromScenarioGraph = fromScenarioGraph;
            UsedSyntheticLlmNode = usedSyntheticLlmNode;
        }

        public IReadOnlyList<Step> Steps { get; }
        public bool FromScenarioGraph { get; }
        public bool UsedSyntheticLlmNode { get; }
    }

    /// <param name="allowSyntheticLlmNode">False when the scenario flow is executed as-is (no extra synthetic LlmNode chip).</param>
    public static Result Build(ScenarioDefinition? scenario, string selectedAgentId, bool ingestActive, bool allowSyntheticLlmNode = true)
    {
        var agent = (selectedAgentId ?? string.Empty).Trim();
        if (scenario?.Flow is not { } flow)
            return Legacy(agent, ingestActive);

        var path = BuildSequentialPath(flow);
        if (path.Count == 0)
            return Legacy(agent, ingestActive);

        var steps = new List<Step>();
        foreach (var node in path)
            AppendGraphNodeWithOptionalIngest(steps, node, ingestActive);

        var personaIdsOnPath = steps
            .Where(s => string.Equals(s.NodeKind, "LlmNode", StringComparison.OrdinalIgnoreCase))
            .Select(s => s.PersonaId)
            .Where(pid => !string.IsNullOrWhiteSpace(pid))
            .Select(pid => pid!.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var usedSynthetic = false;
        if (allowSyntheticLlmNode && !string.IsNullOrEmpty(agent) && !personaIdsOnPath.Contains(agent))
        {
            var outIdx = steps.FindIndex(s => string.Equals(s.NodeKind, "Output", StringComparison.OrdinalIgnoreCase));
            if (outIdx < 0)
                steps.Add(SyntheticLlmNodeStep(agent));
            else
                steps.Insert(outIdx, SyntheticLlmNodeStep(agent));
            usedSynthetic = true;
        }

        ApplyLlmNodeChipHighlighting(steps, agent, usedSynthetic);
        return new Result(steps, fromScenarioGraph: true, usedSyntheticLlmNode: usedSynthetic);
    }

    /// <summary>Synthetic ingest chip id per extractor node (parallel flows may need two chips).</summary>
    public static string SyntheticIngestIdForExtractorNode(string extractorGraphNodeId) =>
        IngestStepId + "-" + SanitizeStepIdSuffix(extractorGraphNodeId.Trim());

    /// <summary>Chips from ChatInput along a single sequential chain until Router, Output, or a branch.</summary>
    public static IReadOnlyList<Step> BuildFlowExecutionPlanPrefix(ScenarioFlowDocument flow, bool ingestChipActive)
    {
        var prefixNodes = CollectLinearPrefixNodes(flow);
        return NodesToPlanSteps(prefixNodes, ingestChipActive);
    }

    /// <summary>Chips from a post-router entry through Output (unique sequential path).</summary>
    public static IReadOnlyList<Step> BuildFlowExecutionPlanLinearTail(ScenarioFlowDocument flow, string entryNodeId, bool ingestChipActive)
    {
        var nodes = CollectLinearPathToOutput(flow, entryNodeId.Trim());
        return NodesToPlanSteps(nodes, ingestChipActive);
    }

    /// <summary>Chips for parallel branch starts through a shared Merge then to Output.</summary>
    public static IReadOnlyList<Step> BuildFlowExecutionPlanParallelTail(
        ScenarioFlowDocument flow,
        IReadOnlyList<string> orderedBranchStarts,
        string mergeNodeId,
        bool ingestChipActive)
    {
        var map = (flow.Nodes ?? new List<ScenarioFlowNode>())
            .Where(n => !string.IsNullOrWhiteSpace(n.Id))
            .ToDictionary(n => n.Id.Trim(), n => n, StringComparer.OrdinalIgnoreCase);
        var mergeTrim = mergeNodeId.Trim();
        var steps = new List<Step>();
        foreach (var start in orderedBranchStarts)
        {
            var seg = CollectLinearPathExclusive(flow, map, start.Trim(), mergeTrim);
            foreach (var node in seg)
                AppendGraphNodeWithOptionalIngest(steps, node, ingestChipActive);
        }

        if (map.TryGetValue(mergeTrim, out var mergeNode))
            AppendGraphNodeWithOptionalIngest(steps, mergeNode, ingestChipActive);

        var afterMerge = UniqueSequentialTarget(flow, mergeTrim);
        if (string.IsNullOrEmpty(afterMerge))
            return steps;

        var tail = CollectLinearPathToOutput(flow, afterMerge);
        foreach (var node in tail)
            AppendGraphNodeWithOptionalIngest(steps, node, ingestChipActive);

        return steps;
    }

    private static void AppendGraphNodeWithOptionalIngest(List<Step> steps, ScenarioFlowNode node, bool ingestChipActive)
    {
        steps.Add(MapGraphNode(node));
        if (!IsPersonaExtractorNode(node))
            return;
        steps.Add(new Step(
            SyntheticIngestIdForExtractorNode(node.Id),
            "Apply extractor JSON → disk",
            optional: true,
            active: ingestChipActive,
            nodeKind: "Ingest"));
    }

    private static IReadOnlyList<Step> NodesToPlanSteps(IReadOnlyList<ScenarioFlowNode> nodes, bool ingestChipActive)
    {
        var steps = new List<Step>();
        foreach (var node in nodes)
            AppendGraphNodeWithOptionalIngest(steps, node, ingestChipActive);
        return steps;
    }

    private static List<ScenarioFlowNode> CollectLinearPrefixNodes(ScenarioFlowDocument flow)
    {
        var nodes = flow.Nodes ?? new List<ScenarioFlowNode>();
        var map = nodes
            .Where(n => !string.IsNullOrWhiteSpace(n.Id))
            .ToDictionary(n => n.Id.Trim(), n => n, StringComparer.OrdinalIgnoreCase);
        var chat = nodes.FirstOrDefault(n =>
            string.Equals(n.Type?.Trim(), "ChatInput", StringComparison.OrdinalIgnoreCase));
        if (chat == null || string.IsNullOrWhiteSpace(chat.Id))
            return new List<ScenarioFlowNode>();

        var path = new List<ScenarioFlowNode>();
        var cur = chat.Id.Trim();
        var guard = 0;
        while (guard++ < 256)
        {
            if (!map.TryGetValue(cur, out var node))
                break;
            path.Add(node);
            var t = (node.Type ?? string.Empty).Trim();
            if (string.Equals(t, "Router", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t, "Output", StringComparison.OrdinalIgnoreCase))
                break;

            var outs = OutgoingSequentialEdgesOrdered(flow, cur);
            if (outs.Count != 1)
                break;
            cur = outs[0].ToNodeId.Trim();
        }

        return path;
    }

    private static List<ScenarioFlowNode> CollectLinearPathToOutput(ScenarioFlowDocument flow, string startNodeId)
    {
        var nodes = flow.Nodes ?? new List<ScenarioFlowNode>();
        var map = nodes
            .Where(n => !string.IsNullOrWhiteSpace(n.Id))
            .ToDictionary(n => n.Id.Trim(), n => n, StringComparer.OrdinalIgnoreCase);
        var path = new List<ScenarioFlowNode>();
        var cur = startNodeId.Trim();
        var guard = 0;
        while (guard++ < 256)
        {
            if (!map.TryGetValue(cur, out var node))
                break;
            path.Add(node);
            if (string.Equals(node.Type?.Trim(), "Output", StringComparison.OrdinalIgnoreCase))
                break;
            var outs = OutgoingSequentialEdgesOrdered(flow, cur);
            if (outs.Count != 1)
                break;
            cur = outs[0].ToNodeId.Trim();
        }

        return path;
    }

    /// <summary>Nodes from <paramref name="startNodeId"/> along a single sequential edge per hop until <paramref name="endExclusive"/>.</summary>
    private static List<ScenarioFlowNode> CollectLinearPathExclusive(
        ScenarioFlowDocument flow,
        IReadOnlyDictionary<string, ScenarioFlowNode> map,
        string startNodeId,
        string endExclusive)
    {
        var path = new List<ScenarioFlowNode>();
        var cur = startNodeId.Trim();
        var guard = 0;
        while (guard++ < 256)
        {
            if (string.Equals(cur, endExclusive, StringComparison.OrdinalIgnoreCase))
                break;
            if (!map.TryGetValue(cur, out var node))
                break;
            path.Add(node);
            var outs = OutgoingSequentialEdgesOrdered(flow, cur);
            if (outs.Count != 1)
                break;
            cur = outs[0].ToNodeId.Trim();
        }

        return path;
    }

    private static string? UniqueSequentialTarget(ScenarioFlowDocument flow, string fromId)
    {
        var outs = OutgoingSequentialEdgesOrdered(flow, fromId);
        if (outs.Count != 1)
            return null;
        return outs[0].ToNodeId.Trim();
    }

    private static List<ScenarioFlowEdge> OutgoingSequentialEdgesOrdered(ScenarioFlowDocument flow, string fromId) =>
        (flow.Edges ?? new List<ScenarioFlowEdge>())
            .Where(e =>
                e != null &&
                !string.IsNullOrWhiteSpace(e.FromNodeId) &&
                string.Equals(e.FromNodeId, fromId, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(e.Mode) || string.Equals(e.Mode, "sequential", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(e => e.Id, StringComparer.Ordinal)
            .ToList();

    /// <summary>Index of the <see cref="Step"/> that receives running/done/error for the streamed persona LLM.</summary>
    public static int ResolveRunnerStepIndex(IReadOnlyList<Step> steps, string selectedAgentId)
    {
        var agent = (selectedAgentId ?? string.Empty).Trim();
        for (var i = 0; i < steps.Count; i++)
        {
            if (!string.Equals(steps[i].NodeKind, "LlmNode", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.IsNullOrEmpty(agent) &&
                string.Equals(steps[i].PersonaId, agent, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        for (var i = 0; i < steps.Count; i++)
        {
            if (steps[i].Id.StartsWith(SyntheticLlmNodeStepIdPrefix, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        for (var i = 0; i < steps.Count; i++)
        {
            if (string.Equals(steps[i].NodeKind, "LlmNode", StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static Step SyntheticLlmNodeStep(string agent) =>
        new(
            SyntheticLlmNodeStepIdPrefix + SanitizeStepIdSuffix(agent),
            $"LlmNode ({agent})",
            optional: false,
            active: true,
            nodeKind: "LlmNode",
            personaId: agent);

    private static string SanitizeStepIdSuffix(string agent) =>
        string.Concat(agent.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));

    private static void ApplyLlmNodeChipHighlighting(List<Step> steps, string selectedAgent, bool usedSynthetic)
    {
        for (var i = 0; i < steps.Count; i++)
        {
            var s = steps[i];
            if (!string.Equals(s.NodeKind, "LlmNode", StringComparison.OrdinalIgnoreCase))
                continue;

            var match = !string.IsNullOrEmpty(selectedAgent)
                        && string.Equals(s.PersonaId, selectedAgent, StringComparison.OrdinalIgnoreCase);
            var optional = usedSynthetic
                ? !s.Id.StartsWith(SyntheticLlmNodeStepIdPrefix, StringComparison.OrdinalIgnoreCase)
                : !match;
            var active = !optional;
            steps[i] = new Step(s.Id, s.Label, optional, active, s.NodeKind, s.PersonaId);
        }
    }

    private static Result Legacy(string selectedAgentId, bool ingestActive) =>
        new(
            new[]
            {
                new Step("chatInput", "Chat input", optional: false, active: true, nodeKind: "ChatInput"),
                new Step("router", "Router", optional: true, active: false, nodeKind: "Router"),
                new Step("llmNode", $"LlmNode ({selectedAgentId})", optional: false, active: true, nodeKind: "LlmNode", selectedAgentId),
                new Step(IngestStepId, "Apply extractor JSON → disk", optional: true, active: ingestActive, nodeKind: "Ingest"),
                new Step("output", "Output", optional: false, active: true, nodeKind: "Output")
            },
            fromScenarioGraph: false,
            usedSyntheticLlmNode: false);

    private static Step MapGraphNode(ScenarioFlowNode node)
    {
        var id = node.Id.Trim();
        var type = (node.Type ?? string.Empty).Trim();
        var label = string.IsNullOrWhiteSpace(node.Label) ? type : node.Label.Trim();

        if (string.Equals(type, "ChatInput", StringComparison.OrdinalIgnoreCase))
            return new Step(id, label, optional: false, active: true, nodeKind: "ChatInput");

        if (string.Equals(type, "Router", StringComparison.OrdinalIgnoreCase))
            return new Step(id, label, optional: true, active: false, nodeKind: "Router");

        if (string.Equals(type, "Merge", StringComparison.OrdinalIgnoreCase))
            return new Step(id, label, optional: true, active: false, nodeKind: "Merge");

        if (string.Equals(type, "Output", StringComparison.OrdinalIgnoreCase))
            return new Step(id, label, optional: false, active: true, nodeKind: "Output");

        if (string.Equals(type, "LlmNode", StringComparison.OrdinalIgnoreCase))
        {
            var pid = TryGetPersonaId(node.Config)?.Trim() ?? "";
            var chip = string.IsNullOrEmpty(pid) ? label : $"LlmNode ({pid})";
            // Highlighting is finalized in ApplyLlmNodeChipHighlighting once synthetic step (if any) is known.
            return new Step(id, chip, optional: false, active: true, nodeKind: "LlmNode", string.IsNullOrEmpty(pid) ? null : pid);
        }

        // Unknown node type: still show it so the graph is not silently dropped.
        return new Step(id, label, optional: false, active: true, nodeKind: type.Length > 0 ? type : "Node");
    }

    private static bool IsPersonaExtractorNode(ScenarioFlowNode node)
    {
        if (!string.Equals(node.Type?.Trim(), "LlmNode", StringComparison.OrdinalIgnoreCase))
            return false;
        var pid = TryGetPersonaId(node.Config);
        return string.Equals(pid, "person-extractor", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Deterministic linear path: only <c>sequential</c> edges, first outgoing edge per node ordered by edge id.
    /// Parallel-only regions are not expanded (designer still shows them; Playground remains a single-threaded preview).
    /// </summary>
    private static List<ScenarioFlowNode> BuildSequentialPath(ScenarioFlowDocument flow)
    {
        var nodes = flow.Nodes ?? new List<ScenarioFlowNode>();
        var edges = flow.Edges ?? new List<ScenarioFlowEdge>();
        var map = nodes
            .Where(n => !string.IsNullOrWhiteSpace(n.Id))
            .ToDictionary(n => n.Id.Trim(), n => n, StringComparer.OrdinalIgnoreCase);

        var chat = nodes.FirstOrDefault(n =>
            string.Equals(n.Type?.Trim(), "ChatInput", StringComparison.OrdinalIgnoreCase));
        if (chat == null || string.IsNullOrWhiteSpace(chat.Id))
            return new List<ScenarioFlowNode>();

        static bool IsSequential(ScenarioFlowEdge e) =>
            string.IsNullOrWhiteSpace(e.Mode) || string.Equals(e.Mode, "sequential", StringComparison.OrdinalIgnoreCase);

        var byFrom = edges
            .Where(e => e != null && !string.IsNullOrWhiteSpace(e.FromNodeId))
            .Where(IsSequential)
            .GroupBy(e => e.FromNodeId.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.Id, StringComparer.Ordinal).ToList(), StringComparer.OrdinalIgnoreCase);

        var path = new List<ScenarioFlowNode>();
        var cur = chat.Id.Trim();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var guard = 0; guard < 256 && seen.Add(cur); guard++)
        {
            if (!map.TryGetValue(cur, out var node))
                break;
            path.Add(node);
            if (string.Equals(node.Type?.Trim(), "Output", StringComparison.OrdinalIgnoreCase))
                break;
            if (!byFrom.TryGetValue(cur, out var outs) || outs.Count == 0)
                break;
            cur = outs[0].ToNodeId.Trim();
        }

        return path;
    }

    private static string? TryGetPersonaId(JsonElement? config)
    {
        if (config is not { ValueKind: JsonValueKind.Object } el)
            return null;
        return el.TryGetProperty("personaId", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;
    }
}
