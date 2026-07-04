using AgctorSDK.Host.Models;
using AgctorSDK.Host.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgctorSDK.Host.ViewComponents;

/// <summary>
/// Reusable panel: editable terminal command + preset picker + Run button + output area.
/// </summary>
public class TerminalCommandPanelViewComponent : ViewComponent
{
    private readonly ITerminalCommandService _terminal;

    public TerminalCommandPanelViewComponent(ITerminalCommandService terminal)
    {
        _terminal = terminal;
    }

    public IViewComponentResult Invoke(
        string componentId,
        string? title = null,
        string? description = null,
        string contextType = "actor-runtime",
        string? contextKey = null,
        string? command = null)
    {
        var presets = _terminal.GetPresets(contextType, contextKey);
        var model = new TerminalCommandPanelModel
        {
            ComponentId = componentId,
            Title = title ?? "Terminal command",
            Description = description ?? "Edit and run docker compose commands from the browser. Only validated docker compose commands are allowed.",
            ContextKey = contextKey,
            ContextType = contextType,
            Command = command ?? _terminal.GetDefaultCommand(contextType, contextKey),
            Presets = presets
        };

        return View(model);
    }
}
