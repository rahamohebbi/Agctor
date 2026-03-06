using AgctorSDK.Host.Models;
using Microsoft.AspNetCore.Mvc;

namespace AgctorSDK.Host.ViewComponents;

/// <summary>
/// Reusable dashboard component that renders the CodeGraph actor hierarchy.
/// </summary>
public class ActorTreeViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(
        string componentId,
        string? title = null,
        string? description = null,
        string? emptyMessage = null)
    {
        var model = new ActorTreeComponentModel
        {
            ComponentId = componentId,
            Title = title ?? "Actor tree",
            Description = description ?? "Solution → Project → File → Class → Method actor hierarchy.",
            EmptyMessage = emptyMessage ?? "No actor tree available."
        };

        return View(model);
    }
}
