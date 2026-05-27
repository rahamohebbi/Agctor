using System.Text.Json;
using AgctorSDK.Host.Models;
using AgctorSDK.Host.Services;

namespace AgctorSDK.Host.Services.Visual;

/// <summary>Delegates visual HTTP endpoints to <c>person-visual-ingest</c> (PRD-023c).</summary>
public sealed class VisualIngestToolBridge
{
    private readonly IToolInvoker _tools;

    public VisualIngestToolBridge(IToolInvoker tools)
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
                "person-visual-ingest",
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
