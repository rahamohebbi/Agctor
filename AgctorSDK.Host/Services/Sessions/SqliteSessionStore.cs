using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Sessions.Models;
using Microsoft.Data.Sqlite;

namespace AgctorSDK.Host.Services.Sessions
{
    /// <summary>
    /// SQLite-backed session storage for durable chat memory.
    /// </summary>
    public sealed class SqliteSessionStore : ISessionStore
    {
        private readonly string _connectionString;
        private readonly SemaphoreSlim _dbLock = new(1, 1);

        public SqliteSessionStore(string databasePath)
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

        public async Task<SessionInfo> CreateSessionAsync(string? sessionId = null, string? title = null, CancellationToken cancellationToken = default)
        {
            sessionId = string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString() : sessionId;
            var now = DateTimeOffset.UtcNow;
            var resolvedTitle = string.IsNullOrWhiteSpace(title) ? $"Session {now:yyyy-MM-dd HH:mm:ss}" : title.Trim();

            await _dbLock.WaitAsync(cancellationToken);
            try
            {
                await using var conn = CreateConnection();
                await conn.OpenAsync(cancellationToken);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
INSERT OR IGNORE INTO sessions (session_id, title, created_at, updated_at, turn_count)
VALUES ($id, $title, $createdAt, $updatedAt, 0);";
                cmd.Parameters.AddWithValue("$id", sessionId);
                cmd.Parameters.AddWithValue("$title", resolvedTitle);
                cmd.Parameters.AddWithValue("$createdAt", now.ToString("O"));
                cmd.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            finally
            {
                _dbLock.Release();
            }

            return await GetSessionAsync(sessionId, cancellationToken)
                ?? throw new InvalidOperationException($"Failed to create session '{sessionId}'.");
        }

        public async Task<SessionInfo?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return null;
            }

            await using var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT session_id, title, created_at, updated_at, turn_count
FROM sessions
WHERE session_id = $id;";
            cmd.Parameters.AddWithValue("$id", sessionId);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return new SessionInfo
            {
                SessionId = reader.GetString(0),
                Title = reader.GetString(1),
                CreatedAt = DateTimeOffset.Parse(reader.GetString(2)),
                UpdatedAt = DateTimeOffset.Parse(reader.GetString(3)),
                TurnCount = reader.GetInt32(4)
            };
        }

        public async Task<IReadOnlyList<SessionInfo>> ListSessionsAsync(int limit = 50, int offset = 0, CancellationToken cancellationToken = default)
        {
            limit = limit <= 0 ? 50 : limit;
            offset = offset < 0 ? 0 : offset;

            var results = new List<SessionInfo>();
            await using var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT session_id, title, created_at, updated_at, turn_count
FROM sessions
ORDER BY updated_at DESC
LIMIT $limit OFFSET $offset;";
            cmd.Parameters.AddWithValue("$limit", limit);
            cmd.Parameters.AddWithValue("$offset", offset);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(new SessionInfo
                {
                    SessionId = reader.GetString(0),
                    Title = reader.GetString(1),
                    CreatedAt = DateTimeOffset.Parse(reader.GetString(2)),
                    UpdatedAt = DateTimeOffset.Parse(reader.GetString(3)),
                    TurnCount = reader.GetInt32(4)
                });
            }

            return results;
        }

        public async Task<IReadOnlyList<SessionTurn>> GetTurnsAsync(string sessionId, int? lastTurns = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return Array.Empty<SessionTurn>();
            }

            var turns = new List<SessionTurn>();
            await using var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = lastTurns.HasValue
                ? @"
SELECT turn_id, session_id, sequence, role, content, agent_id, created_at
FROM (
  SELECT turn_id, session_id, sequence, role, content, agent_id, created_at
  FROM session_turns
  WHERE session_id = $id
  ORDER BY sequence DESC
  LIMIT $lastTurns
)
ORDER BY sequence ASC;"
                : @"
SELECT turn_id, session_id, sequence, role, content, agent_id, created_at
FROM session_turns
WHERE session_id = $id
ORDER BY sequence ASC;";

            cmd.Parameters.AddWithValue("$id", sessionId);
            if (lastTurns.HasValue)
            {
                cmd.Parameters.AddWithValue("$lastTurns", lastTurns.Value);
            }

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                turns.Add(new SessionTurn
                {
                    TurnId = reader.GetString(0),
                    SessionId = reader.GetString(1),
                    Sequence = reader.GetInt32(2),
                    Role = ParseRole(reader.GetString(3)),
                    Content = reader.GetString(4),
                    AgentId = reader.IsDBNull(5) ? null : reader.GetString(5),
                    CreatedAt = DateTimeOffset.Parse(reader.GetString(6))
                });
            }

            return turns;
        }

        public async Task<SessionTurn> AppendTurnAsync(SessionTurn turn, CancellationToken cancellationToken = default)
        {
            if (turn == null) throw new ArgumentNullException(nameof(turn));
            if (string.IsNullOrWhiteSpace(turn.SessionId))
            {
                throw new InvalidOperationException("SessionId is required.");
            }
            if (string.IsNullOrWhiteSpace(turn.Content))
            {
                throw new InvalidOperationException("Content is required.");
            }

            await _dbLock.WaitAsync(cancellationToken);
            try
            {
                await EnsureSessionAsync(turn.SessionId, cancellationToken);

                await using var conn = CreateConnection();
                await conn.OpenAsync(cancellationToken);

                int nextSequence;
                await using (var seqCmd = conn.CreateCommand())
                {
                    seqCmd.CommandText = "SELECT COALESCE(MAX(sequence), 0) + 1 FROM session_turns WHERE session_id = $id;";
                    seqCmd.Parameters.AddWithValue("$id", turn.SessionId);
                    var raw = await seqCmd.ExecuteScalarAsync(cancellationToken);
                    nextSequence = Convert.ToInt32(raw);
                }

                var normalized = new SessionTurn
                {
                    TurnId = string.IsNullOrWhiteSpace(turn.TurnId) ? Guid.NewGuid().ToString() : turn.TurnId,
                    SessionId = turn.SessionId,
                    Sequence = nextSequence,
                    Role = turn.Role,
                    Content = turn.Content,
                    AgentId = string.IsNullOrWhiteSpace(turn.AgentId) ? null : turn.AgentId,
                    CreatedAt = turn.CreatedAt == default ? DateTimeOffset.UtcNow : turn.CreatedAt
                };

                await using (var insertCmd = conn.CreateCommand())
                {
                    insertCmd.CommandText = @"
INSERT INTO session_turns (turn_id, session_id, sequence, role, content, agent_id, created_at)
VALUES ($turnId, $sessionId, $sequence, $role, $content, $agentId, $createdAt);";
                    insertCmd.Parameters.AddWithValue("$turnId", normalized.TurnId);
                    insertCmd.Parameters.AddWithValue("$sessionId", normalized.SessionId);
                    insertCmd.Parameters.AddWithValue("$sequence", normalized.Sequence);
                    insertCmd.Parameters.AddWithValue("$role", normalized.Role.ToString());
                    insertCmd.Parameters.AddWithValue("$content", normalized.Content);
                    insertCmd.Parameters.AddWithValue("$agentId", (object?)normalized.AgentId ?? DBNull.Value);
                    insertCmd.Parameters.AddWithValue("$createdAt", normalized.CreatedAt.ToString("O"));
                    await insertCmd.ExecuteNonQueryAsync(cancellationToken);
                }

                await using (var sessionCmd = conn.CreateCommand())
                {
                    sessionCmd.CommandText = @"
UPDATE sessions
SET updated_at = $updatedAt, turn_count = turn_count + 1
WHERE session_id = $sessionId;";
                    sessionCmd.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
                    sessionCmd.Parameters.AddWithValue("$sessionId", normalized.SessionId);
                    await sessionCmd.ExecuteNonQueryAsync(cancellationToken);
                }

                return normalized;
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task<SessionSummary?> GetSummaryAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT session_id, content, last_included_sequence, updated_at
FROM session_summaries
WHERE session_id = $id;";
            cmd.Parameters.AddWithValue("$id", sessionId);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return new SessionSummary
            {
                SessionId = reader.GetString(0),
                Content = reader.GetString(1),
                LastIncludedSequence = reader.GetInt32(2),
                UpdatedAt = DateTimeOffset.Parse(reader.GetString(3))
            };
        }

        public async Task UpsertSummaryAsync(SessionSummary summary, CancellationToken cancellationToken = default)
        {
            if (summary == null) throw new ArgumentNullException(nameof(summary));
            if (string.IsNullOrWhiteSpace(summary.SessionId))
            {
                throw new InvalidOperationException("Summary SessionId is required.");
            }

            await _dbLock.WaitAsync(cancellationToken);
            try
            {
                await EnsureSessionAsync(summary.SessionId, cancellationToken);
                await using var conn = CreateConnection();
                await conn.OpenAsync(cancellationToken);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
INSERT INTO session_summaries (session_id, content, last_included_sequence, updated_at)
VALUES ($sessionId, $content, $lastIncludedSequence, $updatedAt)
ON CONFLICT(session_id)
DO UPDATE SET content = excluded.content,
              last_included_sequence = excluded.last_included_sequence,
              updated_at = excluded.updated_at;";
                cmd.Parameters.AddWithValue("$sessionId", summary.SessionId);
                cmd.Parameters.AddWithValue("$content", summary.Content ?? string.Empty);
                cmd.Parameters.AddWithValue("$lastIncludedSequence", summary.LastIncludedSequence);
                cmd.Parameters.AddWithValue("$updatedAt", (summary.UpdatedAt == default ? DateTimeOffset.UtcNow : summary.UpdatedAt).ToString("O"));
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            finally
            {
                _dbLock.Release();
            }
        }

        private SqliteConnection CreateConnection() => new(_connectionString);

        private void EnsureSchema()
        {
            using var conn = CreateConnection();
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS sessions (
  session_id TEXT PRIMARY KEY,
  title TEXT NOT NULL,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  turn_count INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS session_turns (
  turn_id TEXT PRIMARY KEY,
  session_id TEXT NOT NULL,
  sequence INTEGER NOT NULL,
  role TEXT NOT NULL,
  content TEXT NOT NULL,
  agent_id TEXT NULL,
  created_at TEXT NOT NULL,
  FOREIGN KEY(session_id) REFERENCES sessions(session_id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_session_turns_session_sequence
ON session_turns(session_id, sequence);

CREATE INDEX IF NOT EXISTS idx_session_turns_session_created
ON session_turns(session_id, created_at);

CREATE TABLE IF NOT EXISTS session_summaries (
  session_id TEXT PRIMARY KEY,
  content TEXT NOT NULL,
  last_included_sequence INTEGER NOT NULL,
  updated_at TEXT NOT NULL,
  FOREIGN KEY(session_id) REFERENCES sessions(session_id) ON DELETE CASCADE
);";
            cmd.ExecuteNonQuery();
        }

        private async Task EnsureSessionAsync(string sessionId, CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;
            await using var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT OR IGNORE INTO sessions (session_id, title, created_at, updated_at, turn_count)
VALUES ($id, $title, $createdAt, $updatedAt, 0);";
            cmd.Parameters.AddWithValue("$id", sessionId);
            cmd.Parameters.AddWithValue("$title", $"Session {now:yyyy-MM-dd HH:mm:ss}");
            cmd.Parameters.AddWithValue("$createdAt", now.ToString("O"));
            cmd.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        private static SessionRole ParseRole(string value)
        {
            if (Enum.TryParse<SessionRole>(value, true, out var role))
            {
                return role;
            }
            return SessionRole.User;
        }
    }
}
