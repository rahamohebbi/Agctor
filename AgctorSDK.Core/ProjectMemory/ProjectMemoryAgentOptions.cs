namespace AgctorSDK.Core.ProjectMemory;

/// <summary>
/// Root folder of a portable project (contains <c>.agctor</c>). Bound from configuration.
/// </summary>
public sealed class ProjectMemoryAgentOptions
{
    public string ProjectRoot { get; set; } = "";

    /// <summary>
    /// Selects whether callers use the actor workflow facade or the legacy
    /// direct pipeline. Actor workflow is the default; Direct remains available
    /// as a compatibility override while broader parity coverage grows.
    /// </summary>
    public ProjectMemoryPipelineExecutionMode ExecutionMode { get; set; } = ProjectMemoryPipelineExecutionMode.ActorWorkflow;
}

public enum ProjectMemoryPipelineExecutionMode
{
    Direct = 0,
    ActorWorkflow = 1
}
