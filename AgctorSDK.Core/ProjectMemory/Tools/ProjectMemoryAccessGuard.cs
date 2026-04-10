using System;
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
}
