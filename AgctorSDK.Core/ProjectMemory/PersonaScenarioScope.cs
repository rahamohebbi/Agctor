using System;
using System.IO;
using System.Linq;
using System.Text;

namespace AgctorSDK.Core.ProjectMemory;

/// <summary>
/// Maps a dashboard scenario id to <c>{ProjectRoot}/scenarios/&lt;segment&gt;/</c> so persona pipeline I/O
/// and LlmNode prompts stay aligned per scenario without duplicating a top-level <c>people/</c> name.
/// </summary>
public static class PersonaScenarioScope
{
    /// <summary>Single segment safe for a directory name (no path separators or traversal).</summary>
    public static string SanitizeFolderSegment(string scenarioId)
    {
        if (string.IsNullOrWhiteSpace(scenarioId))
            return "_default";

        var t = scenarioId.Trim().Replace('\\', '/');
        var last = t.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? t;
        var sb = new StringBuilder(Math.Min(last.Length, 120));
        foreach (var c in last)
        {
            if (char.IsLetterOrDigit(c) || c is '_' or '-' or '.')
                sb.Append(c);
        }

        return sb.Length > 0 ? sb.ToString() : "_default";
    }

    /// <summary>
    /// Directory that should contain <c>people/</c> for this scenario. Empty <paramref name="scenarioId"/> keeps legacy <paramref name="projectRoot"/>.
    /// </summary>
    public static string GetEntityWorkspaceRoot(string projectRoot, string? scenarioId)
    {
        var root = Path.GetFullPath(projectRoot.Trim());
        if (string.IsNullOrWhiteSpace(scenarioId))
            return root;

        var seg = SanitizeFolderSegment(scenarioId);
        return Path.GetFullPath(Path.Combine(root, "scenarios", seg));
    }

    /// <summary>True if <paramref name="path"/> is under <paramref name="projectRoot"/> (after normalization).</summary>
    public static bool IsUnderProjectRoot(string projectRoot, string path)
    {
        var pr = Path.GetFullPath(projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var p = Path.GetFullPath(path);
        return p.StartsWith(pr + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || string.Equals(p, pr, StringComparison.OrdinalIgnoreCase);
    }
}
