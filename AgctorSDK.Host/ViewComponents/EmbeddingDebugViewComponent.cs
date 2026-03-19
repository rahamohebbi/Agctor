using AgctorSDK.Host.Models;
using Microsoft.AspNetCore.Mvc;

namespace AgctorSDK.Host.ViewComponents;

/// <summary>
/// Reusable dashboard component that renders embedding diagnostics.
/// </summary>
public class EmbeddingDebugViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(
        string componentId,
        string? title = null,
        string? description = null)
    {
        var model = new EmbeddingDebugComponentModel
        {
            ComponentId = componentId,
            Title = title ?? "Embedding vectors (debug)",
            Description = description ?? "Load stored vectors to inspect or visualize (table + 2D scatter using first two dimensions)."
        };

        return View(model);
    }
}
