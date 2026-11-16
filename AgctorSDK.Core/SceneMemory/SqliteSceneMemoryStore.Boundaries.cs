using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
namespace AgctorSDK.Core.SceneMemory;

public sealed partial class SqliteSceneMemoryStore
{
    public async Task SaveBoundaryAsync(string workspaceId, SceneBoundaryObservation observation, CancellationToken cancellationToken = default)
    {
        if (observation == null || observation.Ending == null || !observation.Ending.Valid() ||
            string.IsNullOrWhiteSpace(observation.RenderKey) || observation.RenderKey.Length > 200 ||
            string.IsNullOrWhiteSpace(observation.Model) || observation.Model.Length > 200 ||
            observation.SampleSeconds == null || observation.SampleSeconds.Count != 3 ||
            observation.SampleSeconds.Any(t => !double.IsFinite(t) || t < 0) ||
            observation.SampleSeconds.Zip(observation.SampleSeconds.Skip(1), (a,b) => b > a).Any(v => !v))
            throw new ArgumentException("Invalid sampled boundary observation.");
        using var connection = await OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        var workspace = await ReadAsync(connection, transaction, workspaceId, cancellationToken);
        var shot = workspace.Prompts?.SingleOrDefault(p => p.Id == observation.ShotId)
            ?? throw new KeyNotFoundException("Shot not found in this workspace.");
        if (observation.MediaDurationSeconds is { } duration && (!double.IsFinite(duration) || duration <= 0 || observation.SampleSeconds.Last() > duration))
            throw new ArgumentException("Boundary samples exceed the saved media duration.");
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT INTO scene_boundaries VALUES ($workspace, $shot, $key, $document) ON CONFLICT(workspace_id, shot_id, render_key) DO UPDATE SET document = excluded.document";
        command.Parameters.AddWithValue("$workspace", workspaceId); command.Parameters.AddWithValue("$shot", observation.ShotId);
        command.Parameters.AddWithValue("$key", observation.RenderKey); command.Parameters.AddWithValue("$document", JsonSerializer.Serialize(observation));
        await command.ExecuteNonQueryAsync(cancellationToken); transaction.Commit();
    }
    public async Task<IReadOnlyList<SceneBoundaryObservation>> ReadBoundariesAsync(string workspaceId, string? shotId = null, string? renderKey = null, CancellationToken cancellationToken = default)
    {
        var workspace = await GetAsync(workspaceId, cancellationToken);
        var shots = (workspace.Prompts ?? []).Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        using var connection = await OpenAsync(cancellationToken); using var command = connection.CreateCommand();
        command.CommandText = "SELECT document FROM scene_boundaries WHERE workspace_id = $workspace AND ($shot IS NULL OR shot_id = $shot) AND ($key IS NULL OR render_key = $key) ORDER BY rowid DESC LIMIT 200";
        command.Parameters.AddWithValue("$workspace", workspaceId); command.Parameters.AddWithValue("$shot", (object?)shotId ?? DBNull.Value);
        command.Parameters.AddWithValue("$key", (object?)renderKey ?? DBNull.Value);
        using var reader = await command.ExecuteReaderAsync(cancellationToken); var result = new List<SceneBoundaryObservation>();
        while (await reader.ReadAsync(cancellationToken)) {
            var item = JsonSerializer.Deserialize<SceneBoundaryObservation>(reader.GetString(0))!;
            if (shots.Contains(item.ShotId)) result.Add(item);
        }
        return result;
    }
}
