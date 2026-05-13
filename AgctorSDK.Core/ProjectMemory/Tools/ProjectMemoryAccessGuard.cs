using System;
using System.IO;
using System.Linq;
using AgctorSDK.Core.ProjectMemory.Models;

namespace AgctorSDK.Core.ProjectMemory.Tools;

/// <summary>
/// Enforces YAML <c>memoryAccess</c> read/write patterns for tools (PRD §19).
/// </summary>
public static class ProjectMemoryAccessGuard
{
    public static bool CanRead(AgentDefinitionSpec spec, string projectRelativePath)
    {
        var p = projectRelativePath.Replace('\\', '/').TrimStart('/');
        return spec.MemoryAccess.Read.Any(g => GlobMatcher.IsMatch(p, g.Trim()));
    }

    public static bool CanWrite(AgentDefinitionSpec spec, string projectRelativePath)
    {
        var p = projectRelativePath.Replace('\\', '/').TrimStart('/');
        if (spec.MemoryAccess.Write.Any(w => w.Contains("memory_intents_only", StringComparison.OrdinalIgnoreCase)))
            return false;

        if (spec.MemoryAccess.Write.Any(w => w.Equals("schema_allowed_targets_only", StringComparison.OrdinalIgnoreCase)))
            return p.StartsWith("people/", StringComparison.OrdinalIgnoreCase) && p.EndsWith(".md", StringComparison.OrdinalIgnoreCase);

        return spec.MemoryAccess.Write.Any(g => GlobMatcher.IsMatch(p, g.Trim()));
    }

    /// <summary>True when <paramref name="absolutePath"/> is under <c>{projectRoot}/.agctor/runtime/</c>.</summary>
    public static bool IsAgctorRuntimePath(string projectRoot, string absolutePath)
    {
        var pr = Path.GetFullPath(projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var allowed = Path.GetFullPath(Path.Combine(pr, ".agctor", "runtime"));
        var p = Path.GetFullPath(absolutePath);
        return p.StartsWith(allowed + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || string.Equals(p, allowed, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when <paramref name="absolutePath"/> stays inside <c>{projectRoot}/.agctor/</c> (schemas, agents, runtime).</summary>
    public static bool IsUnderProjectAgctor(string projectRoot, string absolutePath)
    {
        var pr = Path.GetFullPath(projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var allowed = Path.GetFullPath(Path.Combine(pr, ".agctor"));
        var p = Path.GetFullPath(absolutePath);
        return p.StartsWith(allowed + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || string.Equals(p, allowed, StringComparison.OrdinalIgnoreCase);
    }
}
