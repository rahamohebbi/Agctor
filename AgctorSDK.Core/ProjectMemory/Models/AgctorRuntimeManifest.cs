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

    /// <summary>Optional PRD-019 thresholds for out-of-schema fact prompts and review queue.</summary>
    public OutOfSchemaCaptureOptions? OutOfSchema { get; set; }
}

/// <summary>Runtime tuning for <c>route_miss</c> facts: confirm vs queue vs discard.</summary>
public sealed class OutOfSchemaCaptureOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>At or above this confidence, surface an immediate yes/no question in ingest output.</summary>
    public double ImmediateConfirmationMinConfidence { get; set; } = 0.75;

    /// <summary>Between <see cref="ReviewQueueMinConfidence"/> and <see cref="ImmediateConfirmationMinConfidence"/> (exclusive of immediate band), enqueue for deferred review.</summary>
    public double ReviewQueueMinConfidence { get; set; } = 0.35;

    /// <summary>Below this confidence, proposals are dropped (noise control).</summary>
    public double DiscardBelowConfidence { get; set; } = 0.0;

    /// <summary>
    /// When true (default when omitted), each newly confirmed generic-inbox fact appends a matching
    /// <c>routing-rules.yaml</c> entry so the next ingest routes the same <c>knowledgeType</c>/<c>attribute</c> without asking again.
    /// </summary>
    public bool? LearnRoutingOnApprove { get; set; }
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
