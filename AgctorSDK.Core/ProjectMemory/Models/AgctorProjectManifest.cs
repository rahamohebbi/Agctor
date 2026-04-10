namespace AgctorSDK.Core.ProjectMemory.Models;

/// <summary>
/// Root <c>.agctor/project.yaml</c> — portable project identity and active project type.
/// </summary>
public sealed class AgctorProjectManifest
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Stable id for this project folder (slug or UUID).</summary>
    public string ProjectId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    /// <summary>Active project type key, e.g. <c>people</c>.</summary>
    public string ProjectType { get; set; } = "";

    /// <summary>Optional notes for humans.</summary>
    public string? Description { get; set; }
}
