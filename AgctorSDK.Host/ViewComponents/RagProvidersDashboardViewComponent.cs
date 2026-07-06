using AgctorSDK.Core.Rag;
using AgctorSDK.Host.Models;
using AgctorSDK.Host.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgctorSDK.Host.ViewComponents;

/// <summary>
/// Razor ViewComponent for RAG provider selection, per-provider config, Docker, and test query.
/// </summary>
public class RagProvidersDashboardViewComponent : ViewComponent
{
    private readonly IRagProvidersDashboardService _dashboard;
    private readonly IRagProviderDockerService _docker;

    public RagProvidersDashboardViewComponent(
        IRagProvidersDashboardService dashboard,
        IRagProviderDockerService docker)
    {
        _dashboard = dashboard;
        _docker = docker;
    }

    public async Task<IViewComponentResult> InvokeAsync(string? selectedProviderId = null)
    {
        var status = await _dashboard.GetStatusAsync(HttpContext.RequestAborted).ConfigureAwait(false);
        var health = await _dashboard.GetHealthAsync(HttpContext.RequestAborted).ConfigureAwait(false);

        var selected = selectedProviderId
            ?? status.Configured.DefaultProvider
            ?? RagProviderIds.None;

        var selectedModel = status.Available.FirstOrDefault(a =>
            string.Equals(a.Id, selected, StringComparison.OrdinalIgnoreCase));

        RagProviderDockerStatusDto? dockerStatus = null;
        var dockerMismatch = false;
        if (selectedModel?.RequiresDocker == true)
        {
            var docker = await _docker.GetStatusAsync(selectedModel.Id, HttpContext.RequestAborted).ConfigureAwait(false);
            dockerStatus = RagProvidersDashboardService.MapDocker(docker);
            dockerMismatch = docker.State is not ("running" or "not_applicable");
        }

        var model = new RagProvidersDashboardModel
        {
            Status = status,
            Health = health,
            DockerStatus = dockerStatus,
            SelectedProviderId = selected,
            SelectedModel = selectedModel,
            DockerMismatch = dockerMismatch
        };

        return View(model);
    }
}
