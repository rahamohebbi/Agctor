namespace AgctorSDK.Core.Rag;

/// <summary>Local Docker sidecar status for an external RAG provider (PRD-025).</summary>
public sealed class RagProviderDockerStatus
{
    public string ProviderId { get; set; } = null!;
    public string? ServiceName { get; set; }
    public bool DockerAvailable { get; set; }
    public bool ComposeFileFound { get; set; }
    public string? ComposeFilePath { get; set; }
    public string State { get; set; } = "unknown";
    public string? StatusText { get; set; }
    public string? ContainerId { get; set; }
    public string? ContainerName { get; set; }
    public string? Health { get; set; }
    public string? Message { get; set; }
}

/// <summary>Result of docker compose install/start/stop for a RAG provider.</summary>
public sealed class RagProviderDockerActionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = null!;
    public string? StdOut { get; set; }
    public string? StdErr { get; set; }
}
