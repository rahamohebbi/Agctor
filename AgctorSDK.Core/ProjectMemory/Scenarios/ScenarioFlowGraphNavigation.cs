using System.Text.Json;

namespace AgctorSDK.Core.ProjectMemory.Scenarios;

/// <summary>Lightweight flow-graph navigation helpers for runtime actor resume (JSON-only, no Host dependency).</summary>
public static class ScenarioFlowGraphNavigation
{
    /// <summary>Next node after <c>WaitForInput</c> or <c>AwaitEvent</c>: loopBack edge first, then sequential.</summary>
    public static string ResolveResumeTargetNode(string flowJson, string suspendedNodeId)
    {
        if (string.IsNullOrWhiteSpace(flowJson))
            throw new InvalidOperationException("Flow graph JSON is required.");
        if (string.IsNullOrWhiteSpace(suspendedNodeId))
            throw new InvalidOperationException("Suspended node id is required.");

        using var doc = JsonDocument.Parse(flowJson);
        if (!doc.RootElement.TryGetProperty("edges", out var edges) || edges.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"Suspend node '{suspendedNodeId}' has no outgoing edges.");

        string? loopTarget = null;
        string? seqTarget = null;
        foreach (var edge in edges.EnumerateArray())
        {
            if (!edge.TryGetProperty("fromNodeId", out var fromEl)
                || !string.Equals(fromEl.GetString()?.Trim(), suspendedNodeId.Trim(), StringComparison.OrdinalIgnoreCase))
                continue;

            if (!edge.TryGetProperty("toNodeId", out var toEl))
                continue;
            var to = toEl.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(to))
                continue;

            var mode = edge.TryGetProperty("mode", out var modeEl) ? modeEl.GetString()?.Trim() : "sequential";
            if (string.Equals(mode, "loopBack", StringComparison.OrdinalIgnoreCase))
                loopTarget = to;
            else if (string.Equals(mode, "sequential", StringComparison.OrdinalIgnoreCase) && seqTarget == null)
                seqTarget = to;
        }

        if (!string.IsNullOrWhiteSpace(loopTarget))
            return loopTarget;

        if (!string.IsNullOrWhiteSpace(seqTarget))
            return seqTarget;

        throw new InvalidOperationException($"Suspend node '{suspendedNodeId}' has no loopBack or sequential outgoing edge.");
    }
}
