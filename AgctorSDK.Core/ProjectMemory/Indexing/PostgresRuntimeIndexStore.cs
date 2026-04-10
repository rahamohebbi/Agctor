using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Models;
using Npgsql;

namespace AgctorSDK.Core.ProjectMemory.Indexing;

/// <summary>
/// Same logical schema as SQLite; connection string from <see cref="AgctorRuntimeManifest.Postgres"/>.
/// </summary>
public sealed class PostgresRuntimeIndexStore : IRuntimeIndexStore
{
    private readonly Func<string> _connectionStringResolver;
    private NpgsqlConnection? _connection;

    public PostgresRuntimeIndexStore(Func<string> connectionStringResolver)
    {
        _connectionStringResolver = connectionStringResolver;
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        var cs = _connectionStringResolver();
        _connection?.Dispose();
        _connection = new NpgsqlConnection(cs);
        await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS project_index (
              project_id TEXT PRIMARY KEY,
              project_root TEXT NOT NULL,
              project_type TEXT NOT NULL,
              rebuilt_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS entity_index (
              project_id TEXT NOT NULL,
              entity_key TEXT NOT NULL,
              entity_type TEXT NOT NULL,
              display_name TEXT,
              root_path TEXT NOT NULL,
              PRIMARY KEY (project_id, entity_key)
            );
            CREATE TABLE IF NOT EXISTS document_index (
              project_id TEXT NOT NULL,
              entity_key TEXT NOT NULL,
              rel_path TEXT NOT NULL,
              content_sha256 TEXT,
              PRIMARY KEY (project_id, entity_key, rel_path)
            );
            CREATE TABLE IF NOT EXISTS section_index (
              project_id TEXT NOT NULL,
              entity_key TEXT NOT NULL,
              rel_path TEXT NOT NULL,
              section_title TEXT NOT NULL,
              body TEXT,
              PRIMARY KEY (project_id, entity_key, rel_path, section_title)
            );
            """;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RebuildProjectAsync(
        LoadedProjectContext ctx,
        IReadOnlyList<EntityRecord> entities,
        IDocumentParser parser,
        CancellationToken cancellationToken = default)
    {
        if (_connection == null)
            await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);

        var pid = ctx.Project.ProjectId;
        var root = ctx.ProjectRoot;
        var now = DateTimeOffset.UtcNow.ToString("O");

        await using var tx = await _connection!.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await DeleteProjectAsync(tx, pid, cancellationToken).ConfigureAwait(false);

        await InsertProjectAsync(tx, pid, root, ctx.Project.ProjectType, now, cancellationToken).ConfigureAwait(false);

        foreach (var e in entities)
        {
            await InsertEntityAsync(tx, pid, e, cancellationToken).ConfigureAwait(false);

            foreach (var rel in e.DocumentRelativePaths)
            {
                var full = Path.Combine(e.RootPath, rel);
                var text = File.Exists(full) ? await File.ReadAllTextAsync(full, cancellationToken).ConfigureAwait(false) : "";
                var hash = RuntimeIndexBuilder.Sha256Hex(text);
                await InsertDocumentAsync(tx, pid, e.EntityKey, rel, hash, cancellationToken).ConfigureAwait(false);

                var doc = parser.Parse(text);
                foreach (var s in doc.Sections)
                {
                    await InsertSectionAsync(tx, pid, e.EntityKey, rel, s.Title, s.Body, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task DeleteProjectAsync(NpgsqlTransaction tx, string pid, CancellationToken ct)
    {
        foreach (var table in new[] { "section_index", "document_index", "entity_index", "project_index" })
        {
            await using var c = new NpgsqlCommand($"DELETE FROM {table} WHERE project_id = @p", tx.Connection, tx);
            c.Parameters.AddWithValue("p", pid);
            await c.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private static async Task InsertProjectAsync(NpgsqlTransaction tx, string pid, string root, string type, string when, CancellationToken ct)
    {
        await using var c = new NpgsqlCommand(
            "INSERT INTO project_index(project_id, project_root, project_type, rebuilt_at) VALUES(@a,@b,@c,@d)",
            tx.Connection, tx);
        c.Parameters.AddWithValue("a", pid);
        c.Parameters.AddWithValue("b", root);
        c.Parameters.AddWithValue("c", type);
        c.Parameters.AddWithValue("d", when);
        await c.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task InsertEntityAsync(NpgsqlTransaction tx, string pid, EntityRecord e, CancellationToken ct)
    {
        await using var c = new NpgsqlCommand(
            "INSERT INTO entity_index(project_id, entity_key, entity_type, display_name, root_path) VALUES(@a,@b,@c,@d,@e)",
            tx.Connection, tx);
        c.Parameters.AddWithValue("a", pid);
        c.Parameters.AddWithValue("b", e.EntityKey);
        c.Parameters.AddWithValue("c", e.EntityType);
        c.Parameters.AddWithValue("d", e.Metadata.DisplayName);
        c.Parameters.AddWithValue("e", e.RootPath);
        await c.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task InsertDocumentAsync(NpgsqlTransaction tx, string pid, string entityKey, string rel, string hash, CancellationToken ct)
    {
        await using var c = new NpgsqlCommand(
            "INSERT INTO document_index(project_id, entity_key, rel_path, content_sha256) VALUES(@a,@b,@c,@d)",
            tx.Connection, tx);
        c.Parameters.AddWithValue("a", pid);
        c.Parameters.AddWithValue("b", entityKey);
        c.Parameters.AddWithValue("c", rel);
        c.Parameters.AddWithValue("d", hash);
        await c.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task InsertSectionAsync(NpgsqlTransaction tx, string pid, string entityKey, string rel, string title, string body, CancellationToken ct)
    {
        await using var c = new NpgsqlCommand(
            "INSERT INTO section_index(project_id, entity_key, rel_path, section_title, body) VALUES(@a,@b,@c,@d,@e)",
            tx.Connection, tx);
        c.Parameters.AddWithValue("a", pid);
        c.Parameters.AddWithValue("b", entityKey);
        c.Parameters.AddWithValue("c", rel);
        c.Parameters.AddWithValue("d", title);
        c.Parameters.AddWithValue("e", body);
        await c.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection != null)
            await _connection.DisposeAsync().ConfigureAwait(false);
        _connection = null;
    }
}
