using AgctorSDK.Host.Models;
using Microsoft.AspNetCore.Mvc;

namespace AgctorSDK.Host.ViewComponents;

/// <summary>
/// Reusable dashboard component that renders CodeGraph embedding store controls.
/// </summary>
public class EmbeddingStoreViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(
        string componentId,
        string? title = null,
        string? description = null)
    {
        var model = new EmbeddingStoreComponentModel
        {
            ComponentId = componentId,
            Title = title ?? "Embedding store",
            Description = description ?? "Code vectors stored for semantic search (e.g. by Indexer and Search agents)."
        };

        return View(model);
    }
}
