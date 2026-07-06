namespace AgctorSDK.Host.Models;

/// <summary>View model for the RAG providers dashboard ViewComponent (PRD-025).</summary>
public sealed class RagProvidersDashboardModel
{
    public RagProviderStatusResponseDto Status { get; set; } = null!;
    public RagProviderHealthResponseDto? Health { get; set; }
    public RagProviderDockerStatusDto? DockerStatus { get; set; }
    public string SelectedProviderId { get; set; } = "None";
    public AvailableRagProviderDto? SelectedModel { get; set; }

    /// <summary>Docker-backed provider selected but sidecar is not running.</summary>
    public bool DockerMismatch { get; set; }
}
