namespace AgctorSDK.Host.Models;

/// <summary>GET /api/tools/for-persona/{personaId} — YAML allow/deny + catalog options for dashboard UI.</summary>
public sealed class PersonaHostToolsResponseDto
{
    public string PersonaId { get; set; } = "";

    public string? AgentLabel { get; set; }

    public bool AgentFound { get; set; }

    public IReadOnlyList<string> YamlAllow { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> YamlDeny { get; set; } = Array.Empty<string>();

    /// <summary>Host HTTP tools this persona may use on LlmNode steps (subset of full catalog).</summary>
    public IReadOnlyList<PersonaHostToolOptionDto> HostTools { get; set; } = Array.Empty<PersonaHostToolOptionDto>();

    public IReadOnlyList<PersonaSemanticToolOptionDto> SemanticTools { get; set; } = Array.Empty<PersonaSemanticToolOptionDto>();

    /// <summary>YAML allow tokens that are neither host nor known semantic ops.</summary>
    public IReadOnlyList<string> CustomAllowTokens { get; set; } = Array.Empty<string>();
}

public sealed class PersonaHostToolOptionDto
{
    public string Id { get; set; } = "";

    public string Group { get; set; } = "";

    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    public bool IsAllowed { get; set; }

    /// <summary>YAML token that matched this host tool, when different from <see cref="Id"/>.</summary>
    public string? MatchedYamlToken { get; set; }
}

public sealed class PersonaSemanticToolOptionDto
{
    public string Id { get; set; } = "";

    public string Label { get; set; } = "";

    public bool IsAllowed { get; set; }
}
