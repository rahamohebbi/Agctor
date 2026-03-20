using System.Text.Json;
using AgctorSDK.Host.Models;
using Microsoft.Data.Sqlite;

namespace AgctorSDK.Host.Services.Traces
{
    /// <summary>
    /// SQLite-backed timeline snapshot store.
    /// This acts as a small durable trace backend for the dashboard.
    /// </summary>
    public sealed class SqliteTraceTimelineStore : ITraceTimelineStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private readonly string _connectionString;
        private readonly SemaphoreSlim _dbLock = new(1, 1);

        public SqliteTraceTimelineStore(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
            {
                throw new ArgumentException("Database path is required.", nameof(databasePath));
            }

            var fullPath = Path.GetFullPath(databasePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            _connectionString = new SqliteConnectionStringBuilder { DataSource = fullPath }.ToString();
            EnsureSchema();
        }

        public async Task SaveAsync(TraceTimelineResponse timeline, CancellationToken cancellationToken = default)
        {
            if (timeline == null) throw new ArgumentNullException(nameof(timeline));
            if (string.IsNullOrWhiteSpace(timeline.TraceId))
            {
                throw new InvalidOperationException("TraceId is required.");
            }

            var normalized = new TraceTimelineResponse
            {
                TraceId = timeline.TraceId,
                StartedAtUtc = timeline.StartedAtUtc,
                TotalDurationMs = timeline.TotalDurationMs,
                ExternalViewerUrl = timeline.ExternalViewerUrl,
                Events = timeline.Events ?? new List<TraceTimelineEventDto>()
            };

            var now = DateTimeOffset.UtcNow;
            var json = JsonSerializer.Serialize(normalized, JsonOptions);

            await _dbLock.WaitAsync(cancellationToken);
            try
            {
                await using var conn = CreateConnection();
                await conn.OpenAsync(cancellationToken);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
INSERT INTO trace_timelines (trace_id, timeline_json, created_at, updated_at)
VALUES ($traceId, $timelineJson, $createdAt, $updatedAt)
ON CONFLICT(trace_id)
DO UPDATE SET timeline_json = excluded.timeline_json,
              updated_at = excluded.updated_at;";
                cmd.Parameters.AddWithValue("$traceId", normalized.TraceId);
                cmd.Parameters.AddWithValue("$timelineJson", json);
                cmd.Parameters.AddWithValue("$createdAt", now.ToString("O"));
                cmd.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task<TraceTimelineResponse?> GetAsync(string traceId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(traceId))
            {
                return null;
            }

            await using var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT timeline_json
FROM trace_timelines
WHERE trace_id = $traceId
LIMIT 1;";
            cmd.Parameters.AddWithValue("$traceId", traceId);

            var raw = await cmd.ExecuteScalarAsync(cancellationToken) as string;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            return JsonSerializer.Deserialize<TraceTimelineResponse>(raw, JsonOptions);
        }

        private SqliteConnection CreateConnection() => new(_connectionString);

        private void EnsureSchema()
        {
            using var conn = CreateConnection();
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS trace_timelines (
  trace_id TEXT PRIMARY KEY,
  timeline_json TEXT NOT NULL,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);";
            cmd.ExecuteNonQuery();
        }
    }
}
