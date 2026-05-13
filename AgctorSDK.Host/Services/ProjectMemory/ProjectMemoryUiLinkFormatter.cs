using Microsoft.AspNetCore.Http;

namespace AgctorSDK.Host.Services.ProjectMemory;

/// <summary>
/// Rewrites dashboard deep links in pipeline text so clients get clickable absolute URLs for the current Host.
/// </summary>
public static class ProjectMemoryUiLinkFormatter
{
    private const string WorkspaceRelativeMarker = "Workspace: /Dashboard/ProjectMemory/Workspace?path=";

    /// <summary>
    /// Prefixes <c>Workspace: /Dashboard/ProjectMemory/Workspace?path=…</c> with <c>{scheme}://{host}</c> when absent.
    /// </summary>
    public static string WithAbsoluteWorkspaceLinks(string? text, HttpRequest request)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(request.Host.Value))
            return text ?? "";

        var origin = $"{request.Scheme}://{request.Host.Value}".TrimEnd('/');
        return text.Replace(
            WorkspaceRelativeMarker,
            "Workspace: " + origin + "/Dashboard/ProjectMemory/Workspace?path=",
            StringComparison.Ordinal);
    }
}
