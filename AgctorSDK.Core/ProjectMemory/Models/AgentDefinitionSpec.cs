using System.Collections.Generic;

namespace AgctorSDK.Core.ProjectMemory.Models;

/// <summary>
/// Portable <c>*.agent.yaml</c> role definition (PRD §8).
/// </summary>
public sealed class AgentDefinitionSpec
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    public string Description { get; set; } = "";

    public List<string> ProjectTypes { get; set; } = new();

    public List<string>? ToolBundles { get; set; }

    public List<string> Instructions { get; set; } = new();

    public ContractRef Input { get; set; } = new();
    public ContractRef Output { get; set; } = new();

    public ToolPolicy Tools { get; set; } = new();
    public MemoryAccessPolicy MemoryAccess { get; set; } = new();
    public List<string> Guardrails { get; set; } = new();
    public RuntimeHints? RuntimeHints { get; set; }

    /// <summary>Resolved absolute path on disk when loaded.</summary>
    public string? SourcePath { get; set; }
}

public sealed class ContractRef
{
    public string Type { get; set; } = "";
}

public sealed class ToolPolicy
{
    public List<string> Allow { get; set; } = new();
    public List<string> Deny { get; set; } = new();
}

public sealed class MemoryAccessPolicy
{
    public List<string> Read { get; set; } = new();
    public List<string> Write { get; set; } = new();
}

public sealed class RuntimeHints
{
    public string? PreferredModel { get; set; }
    public string? PreferredMode { get; set; }
}
