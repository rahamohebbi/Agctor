using System.Collections.Generic;

namespace AgctorSDK.Core.ProjectMemory.Models;

public sealed class WorkspaceSchema
{
    public WorkspaceRoot Workspace { get; set; } = new();
}

public sealed class WorkspaceRoot
{
    public List<string> Roots { get; set; } = new();
    public List<EntityViewDef>? EntityViews { get; set; }
    public List<IndexViewDef>? IndexViews { get; set; }
}

public sealed class EntityViewDef
{
    public string EntityType { get; set; } = "";
    public string FolderPattern { get; set; } = "";
    public List<string> Documents { get; set; } = new();
}

public sealed class IndexViewDef
{
    public string Path { get; set; } = "";
    public string Purpose { get; set; } = "";
}
