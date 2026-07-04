namespace AgctorSDK.Core.Runtime;

/// <summary>Local Docker sidecar status for an actor runtime backend.</summary>
public sealed class ActorRuntimeDockerStatus
{
    public string RuntimeId { get; set; } = null!;
    public string? ServiceName { get; set; }
    public bool DockerAvailable { get; set; }
    public bool ComposeFileFound { get; set; }
    public string? ComposeFilePath { get; set; }
    public string State { get; set; } = "unknown";
    /// <summary>Human-readable status from docker compose (e.g. "Up 2 minutes (healthy)").</summary>
    public string? StatusText { get; set; }
    public string? ContainerId { get; set; }
    public string? ContainerName { get; set; }
    public string? Health { get; set; }
    public string? Message { get; set; }
}

/// <summary>Result of a docker compose action (install/start/stop).</summary>
public sealed class ActorRuntimeDockerActionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = null!;
    public string? StdOut { get; set; }
    public string? StdErr { get; set; }
}
