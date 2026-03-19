using AgctorSDK.Host.Models;
using Microsoft.AspNetCore.Mvc;

namespace AgctorSDK.Host.ViewComponents;

/// <summary>
/// Reusable dashboard component that renders the CodeGraph chat workflow.
/// </summary>
public class AgentChatViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(
        string componentId,
        string? title = null,
        string? helpText = null)
    {
        var model = new AgentChatComponentModel
        {
            ComponentId = componentId,
            Title = title ?? "Chat with agents",
            HelpText = helpText ?? "query-agent answers questions about indexed code. Use coder-agent to write or edit code, and refactor-agent for refactors. Click Index now before asking code questions."
        };

        return View(model);
    }
}
