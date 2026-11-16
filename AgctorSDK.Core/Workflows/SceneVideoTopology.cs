using System;
using System.Collections.Generic;
using System.Linq;

namespace AgctorSDK.Core.Workflows;

/// <summary>
/// Shared scene-video agent topology (Phase A definition). Edit and live views consume the same document.
/// Observed run routes are unioned on top; model-evidence nodes follow <see cref="ModelEvidencePolicy"/>.
/// </summary>
public sealed class SceneVideoTopologyDocument
{
    public string SchemaVersion { get; set; } = "1.0";
    public string GraphId { get; set; } = "scene-video-chain";
    public string Name { get; set; } = "Scene video agents";
    /// <summary>observed-only: model nodes appear when Cynelo reports them, not as editable catalog entries.</summary>
    public string ModelEvidencePolicy { get; set; } = "observed-only";
    /// <summary>v1: Audio Engineer present in the fixed roster (Phase D may toggle).</summary>
    public bool AudioEnabled { get; set; } = true;
    public List<SceneVideoTopologyNode> Nodes { get; set; } = new();
    public List<SceneVideoTopologyEdge> Edges { get; set; } = new();
    public SceneVideoTopologyUi? Ui { get; set; }
}

public sealed class SceneVideoTopologyNode
{
    public string Id { get; set; } = "";
    /// <summary>human | agent | tool | llm | memory | model</summary>
    public string Kind { get; set; } = "agent";
    public string? Label { get; set; }
    public string? Detail { get; set; }
    /// <summary>Agent that owns this tool/capability (tools/llm/memory only).</summary>
    public string? OwnerAgentId { get; set; }
    /// <summary>Legacy observed route ids that map to this node (e.g. Continuity critic → Visual critic).</summary>
    public List<string>? Aliases { get; set; }
}

public sealed class SceneVideoTopologyEdge
{
    public string Id { get; set; } = "";
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string? Intent { get; set; }
}

public sealed class SceneVideoTopologyUi
{
    public Dictionary<string, SceneVideoTopologyLayout>? NodeLayouts { get; set; }
}

public sealed class SceneVideoTopologyLayout
{
    public double X { get; set; }
    public double Y { get; set; }
}

/// <summary>Structural checks for the fixed scene-video roster (Phase A).</summary>
public static class SceneVideoTopologyValidator
{
    public static readonly string[] RequiredAgents =
    [
        "Director", "Audio Engineer", "Visual critic", "Planner",
        "Project agent", "Render agent", "Scene Memory agent", "Editor agent"
    ];

    public static IReadOnlyList<string> Validate(SceneVideoTopologyDocument? doc)
    {
        var errors = new List<string>();
        if (doc == null) { errors.Add("Topology document is required."); return errors; }
        if (string.IsNullOrWhiteSpace(doc.SchemaVersion)) errors.Add("schemaVersion is required.");
        if (!string.Equals(doc.GraphId, "scene-video-chain", StringComparison.Ordinal))
            errors.Add("graphId must be scene-video-chain.");
        if (string.IsNullOrWhiteSpace(doc.ModelEvidencePolicy)) errors.Add("modelEvidencePolicy is required.");
        if (!string.Equals(doc.ModelEvidencePolicy, "observed-only", StringComparison.OrdinalIgnoreCase))
            errors.Add("modelEvidencePolicy must be observed-only in v1.");

        var byId = new Dictionary<string, SceneVideoTopologyNode>(StringComparer.Ordinal);
        foreach (var node in doc.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id)) { errors.Add("Every node needs an id."); continue; }
            if (!byId.TryAdd(node.Id, node)) errors.Add($"Duplicate node id '{node.Id}'.");
            if (string.IsNullOrWhiteSpace(node.Kind)) errors.Add($"Node '{node.Id}' needs a kind.");
        }

        if (!byId.ContainsKey("Human") || byId["Human"].Kind != "human")
            errors.Add("Human node (kind human) is required.");

        foreach (var agent in RequiredAgents)
        {
            if (!byId.TryGetValue(agent, out var node) || node.Kind != "agent")
                errors.Add($"Required agent '{agent}' is missing.");
        }

        // Director must not own Cynelo tools or call them via definition edges.
        if (byId.TryGetValue("Director", out var director))
        {
            foreach (var edge in doc.Edges.Where(e => e.From == "Director"))
            {
                if (!byId.TryGetValue(edge.To, out var target)) continue;
                if (target.Kind is "tool" or "llm" or "memory" or "model")
                    errors.Add($"Director must not own capability edge to '{edge.To}' ({target.Kind}).");
            }
        }

        foreach (var edge in doc.Edges)
        {
            if (string.IsNullOrWhiteSpace(edge.Id)) errors.Add("Every edge needs an id.");
            if (!byId.ContainsKey(edge.From)) errors.Add($"Edge '{edge.Id}' from unknown node '{edge.From}'.");
            if (!byId.ContainsKey(edge.To)) errors.Add($"Edge '{edge.Id}' to unknown node '{edge.To}'.");
        }

        foreach (var node in doc.Nodes.Where(n => n.Kind is "tool" or "llm" or "memory"))
        {
            if (string.IsNullOrWhiteSpace(node.OwnerAgentId))
                errors.Add($"Capability '{node.Id}' needs ownerAgentId.");
            else if (!byId.ContainsKey(node.OwnerAgentId) || byId[node.OwnerAgentId].Kind != "agent")
                errors.Add($"Capability '{node.Id}' ownerAgentId '{node.OwnerAgentId}' must be an agent.");
            else if (string.Equals(node.OwnerAgentId, "Director", StringComparison.Ordinal))
                errors.Add($"Director must not own capability '{node.Id}'.");
        }

        // Definition must not seed model-evidence catalog nodes when policy is observed-only.
        if (doc.Nodes.Any(n => n.Kind == "model"))
            errors.Add("modelEvidencePolicy observed-only forbids model nodes in the definition.");

        if (doc.Ui?.NodeLayouts != null)
        {
            foreach (var key in doc.Ui.NodeLayouts.Keys)
            {
                if (!byId.ContainsKey(key))
                    errors.Add($"Layout refers to unknown node '{key}'.");
            }
        }

        return errors;
    }

    /// <summary>
    /// Phase D v1: edits may only change audioEnabled + layouts — roster/edges/ownership stay fixed.
    /// </summary>
    public static IReadOnlyList<string> ValidateFixedRosterEdit(
        SceneVideoTopologyDocument baseline, SceneVideoTopologyDocument edited)
    {
        var errors = new List<string>(Validate(edited));
        if (baseline == null || edited == null) return errors;

        var baseIds = baseline.Nodes.Select(n => n.Id).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var editIds = edited.Nodes.Select(n => n.Id).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (!baseIds.SequenceEqual(editIds, StringComparer.Ordinal))
            errors.Add("v1 topology edit cannot add or remove roster nodes.");

        foreach (var node in baseline.Nodes)
        {
            var match = edited.Nodes.FirstOrDefault(n => n.Id == node.Id);
            if (match == null) continue;
            if (!string.Equals(match.Kind, node.Kind, StringComparison.Ordinal))
                errors.Add($"Node '{node.Id}' kind is fixed.");
            if (!string.Equals(match.OwnerAgentId ?? "", node.OwnerAgentId ?? "", StringComparison.Ordinal))
                errors.Add($"Node '{node.Id}' ownership is fixed in v1.");
        }

        var baseEdges = baseline.Edges.Select(e => e.From + "\0" + e.To).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var editEdges = edited.Edges.Select(e => e.From + "\0" + e.To).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (!baseEdges.SequenceEqual(editEdges, StringComparer.Ordinal))
            errors.Add("v1 topology edit cannot change agent/tool edges.");

        if (!string.Equals(edited.ModelEvidencePolicy, baseline.ModelEvidencePolicy, StringComparison.OrdinalIgnoreCase))
            errors.Add("modelEvidencePolicy is fixed in v1.");
        if (!string.Equals(edited.GraphId, baseline.GraphId, StringComparison.Ordinal))
            errors.Add("graphId is fixed.");

        return errors;
    }
}

/// <summary>Unions definition skeleton with observed run graph; applies display aliases.</summary>
public static class SceneVideoGraphComposer
{
    public static AgentRunGraph Compose(SceneVideoTopologyDocument definition, AgentRunGraph observed)
    {
        var aliasToCanonical = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in definition.Nodes)
        {
            aliasToCanonical[node.Id] = node.Id;
            if (node.Aliases == null) continue;
            foreach (var alias in node.Aliases.Where(a => !string.IsNullOrWhiteSpace(a)))
                aliasToCanonical[alias] = node.Id;
        }

        string Canon(string id) => aliasToCanonical.TryGetValue(id, out var c) ? c : id;

        var nodes = new Dictionary<string, AgentGraphNode>(StringComparer.Ordinal);
        foreach (var n in definition.Nodes)
        {
            if (n.Kind == "model") continue;
            nodes[n.Id] = new AgentGraphNode(n.Id, n.Kind, n.Detail);
        }

        foreach (var n in observed.Nodes)
        {
            var id = Canon(n.Id);
            var kind = id == "Human" ? "human" : n.Kind;
            var detail = nodes.TryGetValue(id, out var existing) ? existing.Detail ?? n.Detail : n.Detail;
            // Prefer definition kind for skeleton agents/tools; keep observed kind for new model evidence.
            if (nodes.TryGetValue(id, out var defNode) && defNode.Kind is not ("model"))
                nodes[id] = new AgentGraphNode(id, defNode.Kind, detail ?? defNode.Detail);
            else
                nodes[id] = new AgentGraphNode(id, kind, n.Detail ?? detail);
        }

        // Directed keys — undirected collapse would hide Director→specialist when the reply edge lands last.
        var edges = new Dictionary<string, AgentGraphEdge>(StringComparer.Ordinal);
        foreach (var e in definition.Edges)
            edges[e.From + "\0" + e.To] = new AgentGraphEdge(e.From, e.To, e.Intent, "defined", 0, DateTimeOffset.MinValue);
        foreach (var e in observed.Edges)
        {
            var from = Canon(e.From);
            var to = Canon(e.To);
            edges[from + "\0" + to] = new AgentGraphEdge(from, to, e.Operation, e.Outcome, e.Count, e.LastAt);
        }

        var calls = observed.ActiveCalls?.Select(c => c with
        {
            From = Canon(c.From),
            To = Canon(c.To)
        }).ToArray();

        return new AgentRunGraph(nodes.Values.OrderBy(n => n.Id, StringComparer.Ordinal).ToArray(),
            edges.Values.ToArray(), calls);
    }
}
