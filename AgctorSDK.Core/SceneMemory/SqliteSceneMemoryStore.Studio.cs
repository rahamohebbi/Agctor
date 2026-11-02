using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.SceneMemory;

public sealed partial class SqliteSceneMemoryStore
{
    /// <inheritdoc />
    public async Task<SceneWorkspace> ConnectAsync(string name, SceneWorkspaceConnection connection, string? workspaceId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        name = Text(name, "Workspace name", 100);
        connection = new(Text(connection.Application, "Application", 100), Text(connection.ProjectId, "Project ID", 200),
            Text(connection.StoryId, "Story ID", 200));
        using var db = await OpenAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = db.BeginTransaction();
        using var command = db.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT workspace_id FROM scene_workspace_connections WHERE application=$app AND project_id=$project AND story_id=$story";
        command.Parameters.AddWithValue("$app", connection.Application);
        command.Parameters.AddWithValue("$project", connection.ProjectId);
        command.Parameters.AddWithValue("$story", connection.StoryId);
        var existing = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        if (existing != null)
        {
            if (workspaceId != null && workspaceId != existing)
                throw new InvalidOperationException("This external story is already connected to another workspace.");
            return await ReadAsync(db, transaction, existing, cancellationToken).ConfigureAwait(false);
        }
        var workspace = workspaceId == null
            ? new SceneWorkspace(Guid.NewGuid().ToString("N"), name, DateTimeOffset.UtcNow, Array.Empty<SceneEntity>(), Array.Empty<SceneFact>())
            : await ReadAsync(db, transaction, workspaceId, cancellationToken).ConfigureAwait(false);
        if (workspace.Connection != null && workspace.Connection != connection)
            throw new InvalidOperationException("This workspace is already connected to a different external story.");
        workspace = workspace with { Connection = connection };
        command.CommandText = workspaceId == null
            ? "INSERT INTO scene_workspaces(id,document) VALUES ($id,$document)"
            : "UPDATE scene_workspaces SET document=$document WHERE id=$id";
        command.Parameters.AddWithValue("$id", workspace.Id);
        command.Parameters.AddWithValue("$document", JsonSerializer.Serialize(workspace));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        command.CommandText = "INSERT INTO scene_workspace_connections(application,project_id,story_id,workspace_id) VALUES ($app,$project,$story,$id)";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return workspace;
    }

    /// <inheritdoc />
    public Task<SceneWorkspace> SyncStudioAsync(string workspaceId, string instanceKey, SceneStudioInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        instanceKey = Text(instanceKey, "Instance key", 200);
        var assetId = Text(input.AssetId, "Asset ID", 200);
        var name = Text(input.Name, "Studio name", 100);
        var kind = new[] { "Character", "Object", "Location" }.FirstOrDefault(k => string.Equals(k, input.Kind?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("Studio kind must be Character, Object or Location.");
        if (input.Version < 1) throw new ArgumentException("Studio version must be a positive, increasing integer.");
        if (input.Defaults?.Count > 20) throw new ArgumentException("Supply at most 20 Studio defaults.");
        var defaults = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in input.Defaults ?? new Dictionary<string, string>())
        {
            var key = Text(pair.Key, "Default name", 100);
            if (!defaults.TryAdd(key, Text(pair.Value, "Default value", 1000)))
                throw new ArgumentException("Studio default names must be unique after trimming.");
        }
        var binding = new SceneStudioBinding(instanceKey, assetId, input.Version, name, defaults);
        return ChangeAsync(workspaceId, workspace =>
        {
            if (workspace.Connection == null) throw new InvalidOperationException("Connect an external story before syncing Studio entities.");
            var entities = workspace.Entities.ToList();
            var existing = entities.SingleOrDefault(e => e.Studio?.InstanceKey == instanceKey);
            if (existing != null)
            {
                if (input.EntityId != null && existing.Id != input.EntityId)
                    throw new InvalidOperationException("This Studio instance is already linked to another entity.");
                if (existing.Kind != kind || existing.Studio!.AssetId != assetId)
                    throw new InvalidOperationException("An existing Studio instance cannot change its kind or asset identity.");
                if (input.Version < existing.Studio.Version) throw new InvalidOperationException("A newer Studio version is already saved.");
                if (input.Version == existing.Studio.Version)
                {
                    if (name != existing.Studio.DisplayName || defaults.Count != existing.Studio.Defaults.Count ||
                        defaults.Any(p => !existing.Studio.Defaults.TryGetValue(p.Key, out var value) || value != p.Value))
                        throw new InvalidOperationException("This Studio version already exists with different content. Increase the version.");
                    return workspace; // Exact retries do not invalidate outstanding previews.
                }
            }
            else if (input.EntityId != null)
            {
                existing = entities.SingleOrDefault(e => e.Id == input.EntityId)
                    ?? throw new KeyNotFoundException("The entity to link was not found in this workspace.");
                if (existing.Studio != null) throw new InvalidOperationException("This entity is already linked to a Studio instance.");
                if (existing.Kind != kind) throw new InvalidOperationException("The Studio kind must match the discovered entity kind.");
            }
            else
            {
                if (entities.Any(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("This name already exists. Supply its entityId to link it explicitly, or choose a distinct instance name.");
                existing = new SceneEntity(Guid.NewGuid().ToString("N"), name, kind, CreatedAtSequence: workspace.History.Count);
                entities.Add(existing);
            }
            // The scene name remains stable: historical fact values can contain that name.
            entities[entities.FindIndex(e => e.Id == existing.Id)] = existing with { Studio = binding };
            return workspace with { Entities = entities.ToArray() };
        }, cancellationToken);
    }
}
