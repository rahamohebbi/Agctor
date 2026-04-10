namespace AgctorSDK.Core.ProjectMemory.Models;

/// <summary>
/// <c>.agctor/runtime.yaml</c> — where rebuildable indexes live (SQLite/Postgres).
/// </summary>
public sealed class AgctorRuntimeManifest
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary><c>sqlite</c> or <c>postgres</c>.</summary>
    public string Mode { get; set; } = "sqlite";

    public SqliteRuntimeOptions? Sqlite { get; set; }

    public PostgresRuntimeOptions? Postgres { get; set; }
}

public sealed class SqliteRuntimeOptions
{
    /// <summary>Path relative to project root, e.g. <c>.agctor/runtime/sqlite/agctor.db</c>.</summary>
    public string DatabasePath { get; set; } = ".agctor/runtime/sqlite/agctor.db";
}

public sealed class PostgresRuntimeOptions
{
    /// <summary>Full connection string or <c>env:VAR_NAME</c> to read at runtime.</summary>
    public string ConnectionString { get; set; } = "";
}
