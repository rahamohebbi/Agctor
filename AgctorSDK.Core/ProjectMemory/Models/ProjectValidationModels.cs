using System.Collections.Generic;
using System.IO;

namespace AgctorSDK.Core.ProjectMemory.Models;

public sealed class LoadedProjectContext
{
    public string ProjectRoot { get; set; } = "";
    public string AgctorRoot => Path.Combine(ProjectRoot, ".agctor");

    public AgctorProjectManifest Project { get; set; } = new();
    public AgctorRuntimeManifest Runtime { get; set; } = new();

    public ProjectTypeBundle TypeSchema { get; set; } = new();
    public IReadOnlyList<AgentDefinitionSpec> AgentSpecs { get; set; } = System.Array.Empty<AgentDefinitionSpec>();

    /// <summary>Absolute paths to loaded schema YAML files (for editors/API).</summary>
    public ResolvedSchemaPaths? ResolvedSchemaPaths { get; set; }
}

/// <summary>Paths to each schema file on disk for the active project type.</summary>
public sealed class ResolvedSchemaPaths
{
    public string ProjectTypeYaml { get; init; } = "";
    public string EntityTypesYaml { get; init; } = "";
    public string DocumentTypesYaml { get; init; } = "";
    public string RoutingRulesYaml { get; init; } = "";
    public string WorkspaceSchemaYaml { get; init; } = "";
}

public sealed class ProjectTypeBundle
{
    public ProjectTypeDefinition ProjectType { get; set; } = new();
    public EntityTypesSchema EntityTypes { get; set; } = new();
    public DocumentTypesSchema DocumentTypes { get; set; } = new();
    public RoutingRulesSchema Routing { get; set; } = new();
    public WorkspaceSchema Workspace { get; set; } = new();
}

public sealed class ValidationIssue
{
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Path { get; set; }
    public bool IsError { get; set; } = true;
}

public sealed class RebuildReport
{
    public bool Success { get; set; }
    public List<ValidationIssue> Issues { get; set; } = new();
    public string? LogPath { get; set; }
}
