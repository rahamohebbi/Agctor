using System.Text.Json.Serialization;

namespace AgctorSDK.Host.Services.Scenarios;

/// <summary>
/// Top-level document for scenario catalog files.
/// </summary>
public sealed class ScenarioCatalogDocument
{
    public int Version { get; set; } = 1;
    public List<ScenarioDefinition> Scenarios { get; set; } = new();

    /// <summary>Default-catalog scenario ids excluded from the merged view (user removed them).</summary>
    public List<string>? SuppressedDefaultScenarioIds { get; set; }
}

/// <summary>
/// One scenario entry loaded from JSON.
/// </summary>
public sealed class ScenarioDefinition
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Kind { get; set; } = ScenarioKinds.Declarative;
    public string? Handler { get; set; }
    public List<string> AgentTypes { get; set; } = new();
    /// <summary>YAML persona ids (kind=project-memory-yaml) attached to this scenario.</summary>
    public List<string> PersonaAgentIds { get; set; } = new();
    /// <summary>Optional role bindings into <see cref="PersonaAgentIds"/>.</summary>
    public ScenarioPersonaBindings PersonaBindings { get; set; } = new();

    /// <summary>PRD-014 optional persona orchestration graph (portable GraphDocument, not renderer-native JSON).</summary>
    public ScenarioFlowDocument? Flow { get; set; }

    [JsonIgnore]
    public bool IsScripted => string.Equals(Kind, ScenarioKinds.Scripted, StringComparison.OrdinalIgnoreCase);
}

public sealed class ScenarioPersonaBindings
{
    public string? Extractor { get; set; }
    public string? Curator { get; set; }
    public string? Query { get; set; }
}

public static class ScenarioKinds
{
    public const string Declarative = "declarative";
    public const string Scripted = "scripted";
}

public sealed class ScenarioCatalogOptions
{
    public string DefaultFile { get; set; } = "Config/agctor-scenarios.json";
    public string UserFile { get; set; } = "Config/agctor-scenarios.user.json";
}

