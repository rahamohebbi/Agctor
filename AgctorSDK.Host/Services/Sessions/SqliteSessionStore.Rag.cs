using System.Text.Json;
using AgctorSDK.Core.ProjectMemory.Rag;

namespace AgctorSDK.Host.Services.Sessions;

public sealed partial class SqliteSessionStore
{
    private static readonly JsonSerializerOptions RagJson = new(JsonSerializerDefaults.Web);
    public async Task SaveRagInspectionAsync(RagInspectionRecord record, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(record, RagJson);
        await _dbLock.WaitAsync(ct);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(ct);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO rag_inspections(id,session_id,turn_group_id,trace_id,started_at,record_json)
                SELECT $id,$session,$turn,$trace,$started,$json FROM sessions WHERE session_id=$session
                ON CONFLICT(id) DO UPDATE SET record_json=excluded.record_json;
                """;
            cmd.Parameters.AddWithValue("$id", record.Id);
            cmd.Parameters.AddWithValue("$session", record.Correlation.SessionId);
            cmd.Parameters.AddWithValue("$turn", record.Correlation.TurnGroupId);
            cmd.Parameters.AddWithValue("$trace", (object?)record.Correlation.TraceId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$started", record.StartedAt.ToString("O"));
            cmd.Parameters.AddWithValue("$json", json);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally { _dbLock.Release(); }
    }

    public async Task<IReadOnlyList<RagInspectionSummary>> GetRagInspectionsAsync(string sessionId, string? turnGroupId = null, string? traceId = null, CancellationToken ct = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT json_remove(record_json, '$.results', '$.preparedContext', '$.rawResponseJson', '$.stages', '$.query') FROM rag_inspections WHERE session_id=$session
            AND ($turn IS NULL OR turn_group_id=$turn) AND ($trace IS NULL OR trace_id=$trace)
            ORDER BY started_at DESC, id LIMIT 200;
            """;
        cmd.Parameters.AddWithValue("$session", sessionId);
        cmd.Parameters.AddWithValue("$turn", (object?)turnGroupId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$trace", (object?)traceId ?? DBNull.Value);
        var result = new List<RagInspectionSummary>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            if (JsonSerializer.Deserialize<RagInspectionSummary>(reader.GetString(0), RagJson) is { } record) result.Add(record);
        return result;
    }

    public async Task<RagInspectionRecord?> GetRagInspectionAsync(string sessionId, string id, CancellationToken ct = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT record_json FROM rag_inspections WHERE session_id=$session AND id=$id";
        cmd.Parameters.AddWithValue("$session", sessionId);
        cmd.Parameters.AddWithValue("$id", id);
        var json = await cmd.ExecuteScalarAsync(ct) as string;
        return json == null ? null : JsonSerializer.Deserialize<RagInspectionRecord>(json, RagJson);
    }
}
