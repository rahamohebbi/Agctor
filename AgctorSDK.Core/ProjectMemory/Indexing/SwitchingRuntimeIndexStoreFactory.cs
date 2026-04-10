using System;
using AgctorSDK.Core.ProjectMemory.Models;

namespace AgctorSDK.Core.ProjectMemory.Indexing;

/// <summary>
/// Uses <see cref="AgctorRuntimeManifest.Mode"/> to pick SQLite vs Postgres.
/// </summary>
public sealed class SwitchingRuntimeIndexStoreFactory : IRuntimeIndexStoreFactory
{
    private readonly SqliteRuntimeIndexStoreFactory _sqlite;
    private readonly PostgresRuntimeIndexStoreFactory _postgres;

    public SwitchingRuntimeIndexStoreFactory(
        SqliteRuntimeIndexStoreFactory sqlite,
        PostgresRuntimeIndexStoreFactory postgres)
    {
        _sqlite = sqlite;
        _postgres = postgres;
    }

    public IRuntimeIndexStore Create(LoadedProjectContext ctx)
    {
        if (string.Equals(ctx.Runtime.Mode, "postgres", StringComparison.OrdinalIgnoreCase))
            return _postgres.Create(ctx);
        return _sqlite.Create(ctx);
    }
}
