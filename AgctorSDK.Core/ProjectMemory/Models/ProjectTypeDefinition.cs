using System.Collections.Generic;

namespace AgctorSDK.Core.ProjectMemory.Models;

/// <summary><c>schemas/&lt;type&gt;/project-type.yaml</c></summary>
public sealed class ProjectTypeDefinition
{
    public string ProjectType { get; set; } = "";
    public int Version { get; set; } = 1;
    public string DisplayName { get; set; } = "";

    public List<string> EntityTypes { get; set; } = new();
    public List<string> DocumentTypes { get; set; } = new();

    public string? WorkspaceSchemaRef { get; set; }
    public string? RoutingRulesRef { get; set; }
    public string? EntityTypesRef { get; set; }
    public string? DocumentTypesRef { get; set; }
}
