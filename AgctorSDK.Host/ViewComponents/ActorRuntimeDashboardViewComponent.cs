using AgctorSDK.Host.Models;
using AgctorSDK.Host.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgctorSDK.Host.ViewComponents;

/// <summary>
/// Razor ViewComponent for actor model selection, per-model config, and Docker controls.
/// </summary>
public class ActorRuntimeDashboardViewComponent : ViewComponent
{
    private readonly IRuntimeDashboardService _dashboard;
    private readonly IActorRuntimeDockerService _docker;

    public ActorRuntimeDashboardViewComponent(IRuntimeDashboardService dashboard, IActorRuntimeDockerService docker)
    {
        _dashboard = dashboard;
        _docker = docker;
    }

    public async Task<IViewComponentResult> InvokeAsync(string? selectedRuntimeId = null)
    {
        var status = await _dashboard.GetStatusAsync(HttpContext.RequestAborted).ConfigureAwait(false);
        var health = await _dashboard.GetHealthAsync(HttpContext.RequestAborted).ConfigureAwait(false);

        var selected = selectedRuntimeId
            ?? status.Configured.DefaultRuntime
            ?? "InMemory";

        var selectedModel = status.Available.FirstOrDefault(a =>
            string.Equals(a.Id, selected, StringComparison.OrdinalIgnoreCase));

        RuntimeDockerStatusDto? dockerStatus = null;
        if (selectedModel?.RequiresDocker == true)
        {
            var docker = await _docker.GetStatusAsync(selectedModel.Id, HttpContext.RequestAborted).ConfigureAwait(false);
            dockerStatus = RuntimeDashboardService.MapDocker(docker);
        }

        var mismatch = !string.IsNullOrEmpty(status.Current.CanonicalId)
            && !string.IsNullOrEmpty(status.Configured.DefaultRuntime)
            && !string.Equals(status.Current.CanonicalId, status.Configured.DefaultRuntime, StringComparison.OrdinalIgnoreCase);

        var model = new ActorRuntimeDashboardModel
        {
            Status = status,
            Health = health,
            DockerStatus = dockerStatus,
            SelectedRuntimeId = selected,
            SelectedModel = selectedModel,
            Mismatch = mismatch
        };

        return View(model);
    }
}
