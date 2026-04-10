namespace AgctorSDK.Core.ProjectMemory;

/// <summary>
/// Root folder of a portable project (contains <c>.agctor</c>). Bound from configuration.
/// </summary>
public sealed class ProjectMemoryAgentOptions
{
    public string ProjectRoot { get; set; } = "";
}
