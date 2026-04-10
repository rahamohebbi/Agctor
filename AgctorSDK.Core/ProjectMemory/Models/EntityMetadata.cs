using System.Collections.Generic;

namespace AgctorSDK.Core.ProjectMemory.Models;

/// <summary>Per-entity <c>entity.yaml</c> in a canonical folder.</summary>
public sealed class EntityMetadata
{
    public string EntityKey { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public List<string>? Aliases { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public string Status { get; set; } = "active";
}
