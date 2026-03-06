using AgctorSDK.Host.Models;
using Microsoft.AspNetCore.Mvc;

namespace AgctorSDK.Host.ViewComponents;

/// <summary>
/// Reusable dashboard component that renders a trace timeline and exposes a small JS API for refreshing it.
/// </summary>
public class TraceTimelineViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(
        string componentId,
        string? title = null,
        string? emptyMessage = null,
        int heightPx = 360,
        string? initialTraceId = null)
    {
        var model = new TraceTimelineComponentModel
        {
            ComponentId = componentId,
            Title = title ?? "Trace timeline",
            EmptyMessage = emptyMessage ?? "Select a trace to visualize.",
            HeightPx = heightPx,
            InitialTraceId = initialTraceId
        };

        return View(model);
    }
}
