using System;
using System.IO;

namespace AgctorSDK.Core.ProjectMemory.Loading;

internal static class PathResolver
{
    public static string CombineAgctor(string projectRoot, params string[] segments)
    {
        var p = Path.Combine(projectRoot, ".agctor");
        foreach (var s in segments)
            p = Path.Combine(p, s);
        return Path.GetFullPath(p);
    }

    public static string ResolveFromAgctorRoot(string agctorRoot, string? relativeRef)
    {
        if (string.IsNullOrWhiteSpace(relativeRef))
            throw new InvalidOperationException("Missing schema reference.");
        var cleaned = relativeRef.Replace('/', Path.DirectorySeparatorChar).TrimStart('.', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(agctorRoot, cleaned));
    }
}
