using System;
using System.IO;
using AgctorSDK.Core.ProjectMemory.Models;

namespace AgctorSDK.Core.ProjectMemory.Indexing;

public static class RuntimePaths
{
    public static string SqliteDatabaseFile(LoadedProjectContext ctx)
    {
        var rel = ctx.Runtime.Sqlite?.DatabasePath ?? ".agctor/runtime/sqlite/agctor.db";
        return Path.GetFullPath(Path.Combine(ctx.ProjectRoot, rel));
    }

    /// <summary>
    /// Supports raw connection string or <c>env:VAR</c> (PRD runtime manifest).
    /// </summary>
    public static string ResolvePostgresConnectionString(LoadedProjectContext ctx)
    {
        var raw = ctx.Runtime.Postgres?.ConnectionString ?? "";
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException("Postgres connection string missing in runtime.yaml.");
        if (raw.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
        {
            var name = raw.AsSpan(4).Trim().ToString();
            var v = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrEmpty(v))
                throw new InvalidOperationException($"Environment variable '{name}' is not set.");
            return v;
        }

        return raw;
    }
}
