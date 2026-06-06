using System.Text.Json;

namespace AgctorSDK.Core.ProjectMemory.Scenarios;

/// <summary>PRD-024 WaitForInput node helpers (attachment policy).</summary>
public static class ScenarioFlowWaitForInputHelper
{
    /// <summary>True when the node config sets <c>acceptAttachments: true</c>.</summary>
    public static bool AcceptsAttachments(string? flowJson, string nodeId)
    {
        if (string.IsNullOrWhiteSpace(flowJson) || string.IsNullOrWhiteSpace(nodeId))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(flowJson);
            if (!doc.RootElement.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
                return false;

            foreach (var node in nodes.EnumerateArray())
            {
                if (!node.TryGetProperty("id", out var idEl))
                    continue;
                if (!string.Equals(idEl.GetString()?.Trim(), nodeId.Trim(), StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!node.TryGetProperty("config", out var cfg) || cfg.ValueKind != JsonValueKind.Object)
                    return false;

                return cfg.TryGetProperty("acceptAttachments", out var flag)
                       && flag.ValueKind == JsonValueKind.True;
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }
}
