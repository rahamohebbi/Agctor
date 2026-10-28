using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace AgctorSDK.Core.SceneMemory;

/// <summary>Small durable scene store. SQLite transactions serialize changes even across store instances.</summary>
public sealed class SqliteSceneMemoryStore : ISceneMemoryStore
{
    private readonly string _connectionString;
    private static readonly string[] Kinds = { "Character", "Object", "Location", "Audio", "Music", "Other" };

    /// <summary>Creates the database at a host-configured path; callers cannot select paths per request.</summary>
    public SqliteSceneMemoryStore(string databasePath)
    {
        var path = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS scene_workspaces (id TEXT PRIMARY KEY, document TEXT NOT NULL)";
        command.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SceneWorkspace>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT document FROM scene_workspaces ORDER BY rowid";
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<SceneWorkspace>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(Deserialize(reader.GetString(0)));
        return result;
    }

    /// <inheritdoc />
    public async Task<SceneWorkspace> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        var workspace = new SceneWorkspace(Guid.NewGuid().ToString("N"), Text(name, "Workspace name", 100),
            DateTimeOffset.UtcNow, Array.Empty<SceneEntity>(), Array.Empty<SceneFact>());
        using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO scene_workspaces (id, document) VALUES ($id, $document)";
        command.Parameters.AddWithValue("$id", workspace.Id);
        command.Parameters.AddWithValue("$document", JsonSerializer.Serialize(workspace));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return workspace;
    }

    /// <inheritdoc />
    public async Task<SceneWorkspace> GetAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        return await ReadAsync(connection, null, workspaceId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<SceneWorkspace> AddEntityAsync(string workspaceId, string name, string kind, CancellationToken cancellationToken = default)
    {
        name = Text(name, "Entity name", 100);
        var canonicalKind = Kinds.FirstOrDefault(k => string.Equals(k, kind?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("Choose Character, Object, Location, Audio, Music or Other.");
        return ChangeAsync(workspaceId, workspace =>
        {
            if (workspace.Entities.Any(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("An entity with that name already exists in this workspace.");
            return workspace with { Entities = workspace.Entities.Append(new SceneEntity(Guid.NewGuid().ToString("N"), name, canonicalKind)).ToArray() };
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<SceneWorkspace> RecordAsync(string workspaceId, string entityId, string property, string value, string? note, CancellationToken cancellationToken = default)
    {
        property = Text(property, "Property", 100).ToLowerInvariant();
        value = Text(value, "Value", 1000);
        note = note?.Trim() ?? "";
        if (note.Length > 2000) throw new ArgumentException("Event note must be at most 2000 characters.");
        return ChangeAsync(workspaceId, workspace =>
        {
            if (!workspace.Entities.Any(e => e.Id == entityId)) throw new KeyNotFoundException("Entity not found in this workspace.");
            var fact = new SceneFact(workspace.History.Count + 1L, entityId, property, value, note, DateTimeOffset.UtcNow);
            return workspace with { History = workspace.History.Append(fact).ToArray() };
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SceneQueryResult> QueryAsync(string workspaceId, SceneQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var workspace = await GetAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        var sequence = query.AtSequence ?? workspace.History.Count;
        if (sequence < 0 || sequence > workspace.History.Count) throw new ArgumentException("Sequence must be between zero and the latest recorded change.");
        SceneEntity[] matches;
        if (!string.IsNullOrWhiteSpace(query.EntityId))
            matches = workspace.Entities.Where(e => e.Id == query.EntityId).ToArray();
        else
        {
            var question = Text(query.Question, "Question or entity ID", 2000);
            // Literal identity lookup only. No model extracts facts or interprets arbitrary questions.
            matches = workspace.Entities.Where(e => Regex.IsMatch(question,
                @"(?<![\p{L}\p{N}_])" + Regex.Escape(e.Name) + @"(?![\p{L}\p{N}_])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))).ToArray();
        }
        if (matches.Length != 1)
            return new(workspaceId, sequence, matches.Length == 0 ? "not_found" : "ambiguous",
                matches.Length == 0 ? "No matching entity. Use an entity's exact name or select an entity."
                    : "More than one entity matched. Select the entity to query.", matches, Array.Empty<SceneFact>());
        var facts = workspace.History.Where(f => f.EntityId == matches[0].Id && f.Sequence <= sequence)
            .GroupBy(f => f.Property, StringComparer.OrdinalIgnoreCase).Select(g => g.Last()).OrderBy(f => f.Property).ToArray();
        var answer = facts.Length == 0 ? $"No facts recorded for {matches[0].Name} at change {sequence}."
            : $"{matches[0].Name}: " + string.Join("; ", facts.Select(f => $"{f.Property} = {f.Value}")) + ".";
        return new(workspaceId, sequence, facts.Length == 0 ? "no_facts" : "found", answer, matches, facts);
    }

    private async Task<SceneWorkspace> ChangeAsync(string id, Func<SceneWorkspace, SceneWorkspace> change, CancellationToken ct)
    {
        using var connection = await OpenAsync(ct).ConfigureAwait(false);
        // Immediate transaction prevents lost updates when two callers append simultaneously.
        using var transaction = connection.BeginTransaction();
        var workspace = change(await ReadAsync(connection, transaction, id, ct).ConfigureAwait(false));
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE scene_workspaces SET document = $document WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$document", JsonSerializer.Serialize(workspace));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return workspace;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(_connectionString);
        try { await connection.OpenAsync(ct).ConfigureAwait(false); return connection; }
        catch { connection.Dispose(); throw; }
    }

    private static async Task<SceneWorkspace> ReadAsync(SqliteConnection connection, SqliteTransaction? transaction, string id, CancellationToken ct)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT document FROM scene_workspaces WHERE id = $id";
        command.Parameters.AddWithValue("$id", id ?? "");
        var document = await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string
            ?? throw new KeyNotFoundException("Scene workspace not found.");
        return Deserialize(document);
    }

    private static SceneWorkspace Deserialize(string json) => JsonSerializer.Deserialize<SceneWorkspace>(json)
        ?? throw new InvalidDataException("Invalid Scene Memory workspace document.");

    private static string Text(string? value, string label, int max)
    {
        value = value?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Length > max)
            throw new ArgumentException($"{label} must contain 1–{max} characters.");
        return value;
    }
}
