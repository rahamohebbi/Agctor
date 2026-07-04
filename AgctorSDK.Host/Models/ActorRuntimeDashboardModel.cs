namespace AgctorSDK.Host.Models;

/// <summary>View model for the actor-runtime dashboard ViewComponent.</summary>
public sealed class ActorRuntimeDashboardModel
{
    public RuntimeStatusResponseDto Status { get; set; } = null!;
    public RuntimeHealthResponseDto? Health { get; set; }
    public RuntimeDockerStatusDto? DockerStatus { get; set; }
    public string SelectedRuntimeId { get; set; } = "InMemory";
    public AvailableRuntimeDto? SelectedModel { get; set; }
    public bool Mismatch { get; set; }
}
