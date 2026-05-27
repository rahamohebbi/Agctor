using System.Text.Json;
using AgctorSDK.Host.Models;
using AgctorSDK.Host.Services;

namespace AgctorSDK.Host.Services.Visual;

/// <summary>Delegates vision extract/re-extract to <c>person-visual-extract</c> (PRD-023d).</summary>
public sealed class VisualExtractToolBridge
{
    private readonly IToolInvoker _tools;

    public VisualExtractToolBridge(IToolInvoker tools)
    {
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
    }

    public async Task<(bool Ok, JsonElement? Body, string? Error)> InvokeAsync(
        string operation,
        Dictionary<string, object> parameters,
        CancellationToken cancellationToken)
    {
        parameters["operation"] = operation;
        var response = await _tools
            .InvokeToolAsync(
                "person-visual-extract",
                new ToolInvocationRequest { Parameters = parameters },
                cancellationToken)
            .ConfigureAwait(false);

        if (response.Status != ToolExecutionStatus.Success)
            return (false, null, response.ErrorMessage ?? "Tool invocation failed.");

        if (response.Result is JsonElement el)
            return (true, el, null);

        return (false, null, "Unexpected tool result shape.");
    }
}
