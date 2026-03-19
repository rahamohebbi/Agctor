using AgctorSDK.Host.Models;
using Microsoft.AspNetCore.Mvc;

namespace AgctorSDK.Host.ViewComponents;

/// <summary>
/// Reusable dashboard component that renders a raw JSON payload panel.
/// </summary>
public class RawJsonViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(
        string componentId,
        string? title = null)
    {
        var model = new RawJsonComponentModel
        {
            ComponentId = componentId,
            Title = title ?? "Raw JSON"
        };

        return View(model);
    }
}
