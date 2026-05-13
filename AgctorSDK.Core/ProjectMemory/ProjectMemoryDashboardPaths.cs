using System;

namespace AgctorSDK.Core.ProjectMemory;

/// <summary>
/// Stable dashboard routes for the Agctor Host Razor UI (project memory). Core emits relative URLs;
/// the Host may rewrite them to absolute links using the incoming HTTP request.
/// </summary>
public static class ProjectMemoryDashboardPaths
{
    public const string WorkspacePagePath = "/Dashboard/ProjectMemory/Workspace";

    /// <summary>Path + query only (no origin) — safe to embed in API payloads and logs.</summary>
    public static string WorkspaceFileHref(string projectRelativeFilePath)
    {
        var rel = (projectRelativeFilePath ?? "").Trim().Replace('\\', '/').TrimStart('/');
        return WorkspacePagePath + "?path=" + Uri.EscapeDataString(rel);
    }

    /// <summary>Single line for pipeline/chat output; Host replaces the relative href with an absolute URL when possible.</summary>
    public static string WorkspaceDeepLinkLine(string projectRelativeFilePath) =>
        "Workspace: " + WorkspaceFileHref(projectRelativeFilePath);
}
