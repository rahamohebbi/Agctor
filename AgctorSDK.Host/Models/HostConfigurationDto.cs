using AgctorSDK.Host.Services;

namespace AgctorSDK.Host.Models;

/// <summary>
/// Root DTO for Host configuration exposed by GET /api/config (PRD-006).
/// </summary>
public class HostConfigurationDto
{
    public RuntimeConfigDto Runtime { get; set; } = null!;
    public LlmConfigDto Llm { get; set; } = null!;
    public McpConfigDto Mcp { get; set; } = null!;
    public string GeneratedCodeRoot { get; set; } = null!;
    public BackgroundServicesDto BackgroundServices { get; set; } = null!;
    public IReadOnlyDictionary<string, string> AgentTypes { get; set; } = null!;
    /// <summary>Effective enablement per registered agent type key (PRD-010); default true when unset.</summary>
    public IReadOnlyDictionary<string, bool> AgentTypeEnablement { get; set; } = null!;
    /// <summary>Single dashboard scenario name from Agctor:Dashboard:ScenarioName (PRD-010).</summary>
    public string DashboardScenarioName { get; set; } = null!;
    public IReadOnlyList<ToolInfo> Tools { get; set; } = null!;
    public IReadOnlyDictionary<string, string> Scenarios { get; set; } = null!;
}

/// <summary>
/// Runtime adapter name and optional Proto.Actor settings.
/// </summary>
public class RuntimeConfigDto
{
    public string Name { get; set; } = null!;
    public string? ProtoHost { get; set; }
    public int? ProtoPort { get; set; }
}

/// <summary>
/// LLM (Ollama) configuration.
/// </summary>
public class LlmConfigDto
{
    public string OllamaApiUrl { get; set; } = null!;
    public string DefaultModel { get; set; } = null!;
}

/// <summary>
/// MCP listener configuration.
/// </summary>
public class McpConfigDto
{
    public string Host { get; set; } = null!;
    public int Port { get; set; }
}

/// <summary>
/// Background service intervals (TaskScoper, TaskFlow).
/// </summary>
public class BackgroundServicesDto
{
    public int TaskScoperScanIntervalSeconds { get; set; }
    public int TaskFlowIntervalSeconds { get; set; }
}
