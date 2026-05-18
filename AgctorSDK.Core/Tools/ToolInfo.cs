using System.Collections.Generic;

namespace AgctorSDK.Core.Tools;

/// <summary>Discovery metadata for a tool (HTTP catalog and dashboards).</summary>
public class ToolInfo
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Description { get; set; } = null!;
    public Dictionary<string, object> Parameters { get; set; } = new();
    public string Version { get; set; } = "1.0.0";
}
