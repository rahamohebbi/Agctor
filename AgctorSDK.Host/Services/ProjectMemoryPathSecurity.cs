namespace AgctorSDK.Host.Services;

/// <summary>
/// Ensures file operations stay under the configured project root (PRD-013 UX security).
/// </summary>
public static class ProjectMemoryPathSecurity
{
    public static string GetSafeFullPath(string projectRoot, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("Relative path is required.", nameof(relativePath));

        var root = Path.GetFullPath(projectRoot.TrimEnd(Path.DirectorySeparatorChar));
        var rel = relativePath.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        if (rel.Contains(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || rel.StartsWith("..", StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Path must not contain parent segments.");

        var full = Path.GetFullPath(Path.Combine(root, rel));
        var rootWithSep = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Path escapes project root.");

        return full;
    }

    public static string ToRelativePath(string projectRoot, string fullPath)
    {
        var root = Path.GetFullPath(projectRoot.TrimEnd(Path.DirectorySeparatorChar));
        var full = Path.GetFullPath(fullPath);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException();
        var rest = full.Length > root.Length ? full[root.Length..].TrimStart(Path.DirectorySeparatorChar) : "";
        return rest.Replace(Path.DirectorySeparatorChar, '/');
    }
}
