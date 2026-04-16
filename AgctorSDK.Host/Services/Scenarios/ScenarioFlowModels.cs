using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgctorSDK.Host.Services.Scenarios;

/// <summary>
/// PRD-014 canonical portable graph embedded as <c>scenario.flow</c> in catalog JSON and API DTOs.
/// Not a renderer dump; UI positions live under <see cref="Ui"/>.
/// </summary>
public sealed class ScenarioFlowDocument
{
    public string SchemaVersion { get; set; } = "1.0";

    public string GraphId { get; set; } = string.Empty;

    public string? Name { get; set; }

    /// <summary>active | archived | deleted (soft-delete).</summary>
    public string? Status { get; set; }

    public string? CreatedAtUtc { get; set; }

    public string? UpdatedAtUtc { get; set; }

    public string OutputPolicy { get; set; } = "merge_sections";

    public List<ScenarioFlowNode> Nodes { get; set; } = new();

    public List<ScenarioFlowEdge> Edges { get; set; } = new();

    public ScenarioFlowUi? Ui { get; set; }

    /// <summary>Deep clone for catalog merge (avoids shared references).</summary>
    public static ScenarioFlowDocument? Clone(ScenarioFlowDocument? src)
    {
        if (src == null) return null;
        var json = JsonSerializer.Serialize(src, ScenarioFlowJson.Options);
        return JsonSerializer.Deserialize<ScenarioFlowDocument>(json, ScenarioFlowJson.Options);
    }
}

public sealed class ScenarioFlowNode
{
    public string Id { get; set; } = string.Empty;

    /// <summary>ChatInput | Router | PersonaCall | Merge | Output</summary>
    public string Type { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    /// <summary>Type-specific payload (e.g. personaId for PersonaCall).</summary>
    public JsonElement? Config { get; set; }
}

public sealed class ScenarioFlowEdge
{
    public string Id { get; set; } = string.Empty;

    public string FromNodeId { get; set; } = string.Empty;

    public string ToNodeId { get; set; } = string.Empty;

    /// <summary>sequential | parallel</summary>
    public string Mode { get; set; } = "sequential";

    public string? Condition { get; set; }
}

public sealed class ScenarioFlowUi
{
    public Dictionary<string, ScenarioFlowNodeLayout>? NodeLayouts { get; set; }
}

public sealed class ScenarioFlowNodeLayout
{
    public double X { get; set; }

    public double Y { get; set; }
}

/// <summary>Shared serializer options for flow clone and optional file round-trip helpers.</summary>
public static class ScenarioFlowJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
