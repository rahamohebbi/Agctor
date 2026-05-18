namespace AgctorSDK.Host.Models;

/// <summary>GET /api/tools/agent-associations — tools plus who may use them (YAML + C# hints).</summary>
public sealed class ToolAgentsInsightResponse
{
    public DateTimeOffset GeneratedAt { get; set; }
    public IReadOnlyList<ToolInsightDto> Tools { get; set; } = Array.Empty<ToolInsightDto>();
    public IReadOnlyList<UnmappedYamlToolTokenDto> UnmappedYamlAllowTokens { get; set; } = Array.Empty<UnmappedYamlToolTokenDto>();
}

public sealed class ToolInsightDto
{
    /// <summary>CLR registration name (e.g. <c>FileSystemTool</c>).</summary>
    public string ClrTypeName { get; set; } = "";

    /// <summary>HTTP id when exposed on REST (e.g. <c>file-system</c>); null when internal-only.</summary>
    public string? HttpPrimaryId { get; set; }

    public string DisplayName { get; set; } = "";

    /// <summary>Short human summary from catalog discovery metadata (<c>ToolInfo.Description</c>).</summary>
    public string Description { get; set; } = "";

    /// <summary>True when <see cref="AgctorToolCatalog.RegisterToolActorTypes"/> registered this CLR tool on the host factory.</summary>
    public bool IsRegistered { get; set; }

    public IReadOnlyList<ToolAgentAssociationDto> Associations { get; set; } = Array.Empty<ToolAgentAssociationDto>();
}

public sealed class ToolAgentAssociationDto
{
    /// <summary><c>project-memory-yaml</c> or <c>csharp-agent-type</c>.</summary>
    public string Kind { get; set; } = "";

    public string AgentId { get; set; } = "";
    public string AgentLabel { get; set; } = "";

    /// <summary><c>tools.allow</c>, <c>csharp-known-pattern</c>, etc.</summary>
    public string Source { get; set; } = "";

    /// <summary>Original YAML token or explanation for C# rows.</summary>
    public string? Detail { get; set; }
}

public sealed class UnmappedYamlToolTokenDto
{
    public string AgentId { get; set; } = "";
    public string AgentLabel { get; set; } = "";
    public string Token { get; set; } = "";
}

/// <summary>GET /api/agents/definitions/tool-usage — agents with host tools they may invoke (pivot of tool insight).</summary>
public sealed class AgentToolsInsightResponse
{
    public DateTimeOffset GeneratedAt { get; set; }
    public IReadOnlyList<AgentToolsInsightRowDto> Agents { get; set; } = Array.Empty<AgentToolsInsightRowDto>();
}

public sealed class AgentToolsInsightRowDto
{
    public string AgentId { get; set; } = "";
    public string AgentLabel { get; set; } = "";

    /// <summary><c>project-memory-yaml</c> or <c>csharp-agent-type</c>.</summary>
    public string Kind { get; set; } = "";

    public IReadOnlyList<AgentLinkedToolDto> Tools { get; set; } = Array.Empty<AgentLinkedToolDto>();

    /// <summary>YAML <c>tools.allow</c> tokens that did not resolve to a host tool (informational).</summary>
    public IReadOnlyList<string> UnmappedYamlAllowTokens { get; set; } = Array.Empty<string>();
}

public sealed class AgentLinkedToolDto
{
    public string ClrTypeName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? HttpPrimaryId { get; set; }
    public string Description { get; set; } = "";
    public string Source { get; set; } = "";
    public string? Detail { get; set; }
}
