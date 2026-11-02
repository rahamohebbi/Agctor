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
public sealed partial class SqliteSceneMemoryStore : ISceneMemoryStore
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
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS scene_workspace_connections (
                application TEXT NOT NULL, project_id TEXT NOT NULL, story_id TEXT NOT NULL,
                workspace_id TEXT NOT NULL UNIQUE,
                PRIMARY KEY (application, project_id, story_id))
            """;
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
        if (property is "held_by" or "holding")
            throw new ArgumentException("Use Set holder / transfer to record possession.");
        value = Text(value, "Value", 1000);
        note = note?.Trim() ?? "";
        if (note.Length > 2000) throw new ArgumentException("Event note must be at most 2000 characters.");
        return ChangeAsync(workspaceId, workspace =>
        {
            var entity = workspace.Entities.FirstOrDefault(e => e.Id == entityId) ?? throw new KeyNotFoundException("Entity not found in this workspace.");
            var normalized = SceneStateRules.Normalize(new(entityId, property, value, "", false, note), entity);
            property = normalized.Property; value = normalized.Value;
            SceneStateRules.Validate(normalized, entity, null, workspace.History.Where(f => !f.IsEvent)
                .GroupBy(f => (f.EntityId, f.Property)).ToDictionary(g => g.Key, g => g.Last()));
            var history = workspace.History.ToList();
            if (ScenePromptReducer.IsTerminalStatus(property, value) && entity.Kind != "Object" && !(entity.Kind == "Location" && value == "destroyed"))
                throw new ArgumentException("Consumed status applies to objects; destroyed status applies to objects or locations.");
            if (entity.Kind == "Object" && (ScenePromptReducer.IsTerminalStatus(property, value) || SceneStateRules.ReleasesObject(property, value)))
            {
                foreach (var old in history.Where(f => f.EntityId == entityId && f.Property is "held_by" or "on" or "in" or "located_at" or "carried_at")
                    .GroupBy(f => f.Property).Select(g => g.Last()).Where(f => !f.Removed).ToArray())
                    history.Add(new SceneFact(history.Count + 1L, entityId, old.Property, "", note, DateTimeOffset.UtcNow, Removed: true));
            }
            if (property == "life_state" && value == "dead")
                foreach (var old in history.Where(f => !f.IsEvent && f.EntityId == entityId && SceneStateRules.IsActivity(f.Property))
                    .GroupBy(f => f.Property).Select(g => g.Last()).Where(f => !f.Removed).ToArray())
                    history.Add(new SceneFact(history.Count + 1L, entityId, old.Property, "", note, DateTimeOffset.UtcNow, Removed: true));
            var fact = new SceneFact(history.Count + 1L, entityId, property, value, note, DateTimeOffset.UtcNow);
            return workspace with { History = history.Append(fact).ToArray() };
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<SceneWorkspace> SetHolderAsync(string workspaceId, string objectId, string holderId, string? note, CancellationToken cancellationToken = default)
    {
        note = note?.Trim() ?? "";
        if (note.Length > 2000) throw new ArgumentException("Event note must be at most 2000 characters.");
        return ChangeAsync(workspaceId, workspace =>
        {
            var item = workspace.Entities.FirstOrDefault(e => e.Id == objectId)
                ?? throw new KeyNotFoundException("Object not found in this workspace.");
            var holder = workspace.Entities.FirstOrDefault(e => e.Id == holderId)
                ?? throw new KeyNotFoundException("Holder not found in this workspace.");
            if (item.Kind != "Object" || holder.Kind != "Character")
                throw new ArgumentException("Choose an Object and a Character as its holder.");
            var status = workspace.History.LastOrDefault(f => f.EntityId == item.Id && f.Property == "status");
            if (status != null && !status.Removed && ScenePromptReducer.IsTerminalStatus(status.Property, status.Value))
                throw new ArgumentException("A consumed or destroyed object cannot be held. Restore its status explicitly or create a different object.");
            var history = workspace.History.ToList();
            var disposition = history.LastOrDefault(f => f.EntityId == item.Id && f.Property == "disposition");
            if (disposition != null && !disposition.Removed && SceneStateRules.ReleasesObject(disposition.Property, disposition.Value))
                history.Add(new SceneFact(history.Count + 1L, item.Id, "disposition", "recovered", note, DateTimeOffset.UtcNow));
            var previousHolder = history.LastOrDefault(f => f.EntityId == item.Id && f.Property == "held_by");
            var carriedAt = history.LastOrDefault(f => f.EntityId == item.Id && f.Property == "carried_at");
            if (carriedAt != null && !carriedAt.Removed && (previousHolder == null || previousHolder.Removed || previousHolder.RelatedEntityId != holder.Id))
                history.Add(new SceneFact(history.Count + 1L, item.Id, "carried_at", "", note, DateTimeOffset.UtcNow, Removed: true));
            foreach (var old in history.Where(f => f.Property is "has" or "holds" or "holding" or "carries" or "carrying")
                .GroupBy(f => (f.EntityId, f.Property)).Select(g => g.Last())
                .Where(f => !f.Removed && (f.RelatedEntityId == item.Id || string.Equals(f.Value, item.Name, StringComparison.OrdinalIgnoreCase))).ToArray())
                history.Add(new SceneFact(history.Count + 1L, old.EntityId, old.Property, "", note, DateTimeOffset.UtcNow, Removed: true));
            foreach (var previous in history.Where(f => f.EntityId == item.Id && f.Property is "on" or "in" or "located_at")
                .GroupBy(f => f.Property).Select(g => g.Last()).Where(f => !f.Removed).ToArray())
                history.Add(new SceneFact(history.Count + 1L, item.Id, previous.Property, "", note, DateTimeOffset.UtcNow, Removed: true));
            var fact = new SceneFact(history.Count + 1L, item.Id, "held_by", holder.Name,
                note, DateTimeOffset.UtcNow, holder.Id);
            return workspace with { History = history.Append(fact).ToArray() };
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<SceneWorkspace> ApplyPromptAsync(string workspaceId, ScenePromptPreview preview, CancellationToken cancellationToken = default) =>
        ChangeAsync(workspaceId, workspace => ScenePromptReducer.Apply(workspace, preview), cancellationToken);

    /// <inheritdoc />
    public Task<SceneWorkspace> UpdatePromptMetadataAsync(string workspaceId, string promptId, string? sceneLabel,
        string? shotLabel, double? durationSeconds, CancellationToken cancellationToken = default)
    {
        sceneLabel = OptionalText(sceneLabel, "Scene name", 100);
        shotLabel = OptionalText(shotLabel, "Shot name", 100);
        if (durationSeconds is <= 0 or > 3600)
            throw new ArgumentException("Shot duration must be between 0.1 seconds and one hour.");
        return ChangeAsync(workspaceId, workspace =>
        {
            var prompts = (workspace.Prompts ?? Array.Empty<ScenePromptRecord>()).ToArray();
            var index = Array.FindIndex(prompts, p => p.Id == promptId);
            if (index < 0) throw new KeyNotFoundException("Shot not found in this workspace.");
            prompts[index] = prompts[index] with { SceneLabel = sceneLabel, Label = shotLabel, DurationSeconds = durationSeconds };
            return workspace with { Prompts = prompts };
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
        var current = workspace.History.Where(f => !f.IsEvent && f.Sequence <= sequence)
            .GroupBy(f => (f.EntityId, Property: f.Property.ToLowerInvariant())).Select(g => g.Last()).Where(f => !f.Removed).ToArray();
        // Possession has one source of truth on the object. Reverse lookup reflects transfers automatically.
        var heldObjects = current.Where(f => f.Property is "held_by" or "owned_by" or "consumed_by" or "destroyed_by" && f.RelatedEntityId == matches[0].Id)
            .Select(f => f with { EntityId = matches[0].Id, Property = f.Property switch { "owned_by" => "owns", "consumed_by" => "consumed", "destroyed_by" => "destroyed", _ => "holding" },
                Value = workspace.Entities.Single(e => e.Id == f.EntityId).Name, RelatedEntityId = f.EntityId });
        var facts = current.Where(f => f.EntityId == matches[0].Id).Concat(heldObjects).OrderBy(f => f.Property).ToArray();
        var answer = facts.Length == 0 ? $"No facts recorded for {matches[0].Name} at change {sequence}."
            : $"{matches[0].Name}: " + string.Join("; ", facts.Select(f => $"{f.Property} = {f.Value}")) + ".";
        var events = workspace.History.Where(f => f.IsEvent && f.Sequence <= sequence &&
            (f.EntityId == matches[0].Id || f.ParticipantIds?.Contains(matches[0].Id) == true)).ToArray();
        if (events.Length > 0)
            answer = (facts.Length == 0 ? $"{matches[0].Name}: no current properties recorded." : answer) +
                "\nEvents: " + string.Join("; ", events.Select(e => $"[{e.EventMode}] {e.Value}"));
        return new(workspaceId, sequence, facts.Length == 0 && events.Length == 0 ? "no_facts" : "found", answer, matches, facts, events);
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

    private static string OptionalText(string? value, string label, int max)
    {
        value = value?.Trim() ?? "";
        if (value.Length > max) throw new ArgumentException($"{label} must be at most {max} characters.");
        return value;
    }
}
