using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.Sessions;
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

        public async Task<SessionInfo> CreateSessionAsync(string? sessionId = null, string? title = null, string? projectId = null, CancellationToken cancellationToken = default)
        {
            sessionId = string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString() : sessionId;
            var now = DateTimeOffset.UtcNow;
            var resolvedTitle = string.IsNullOrWhiteSpace(title) ? $"Session {now:yyyy-MM-dd HH:mm:ss}" : title.Trim();
            var resolvedProjectId = string.IsNullOrWhiteSpace(projectId) ? null : projectId.Trim();

            await _dbLock.WaitAsync(cancellationToken);
            try
            {
                await using var conn = CreateConnection();
                await conn.OpenAsync(cancellationToken);
                if (resolvedProjectId != null && !await ProjectExistsAsync(conn, resolvedProjectId, cancellationToken).ConfigureAwait(false))
                    throw new InvalidOperationException($"Project '{resolvedProjectId}' not found.");

                await using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
INSERT OR IGNORE INTO sessions (session_id, title, project_id, created_at, updated_at, turn_count)
VALUES ($id, $title, $projectId, $createdAt, $updatedAt, 0);";
                cmd.Parameters.AddWithValue("$id", sessionId);
                cmd.Parameters.AddWithValue("$title", resolvedTitle);
                cmd.Parameters.AddWithValue("$projectId", resolvedProjectId is null ? DBNull.Value : resolvedProjectId);
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
SELECT session_id, title, project_id, created_at, updated_at, turn_count
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
                ProjectId = reader.IsDBNull(2) ? null : reader.GetString(2),
                CreatedAt = DateTimeOffset.Parse(reader.GetString(3)),
                UpdatedAt = DateTimeOffset.Parse(reader.GetString(4)),
                TurnCount = reader.GetInt32(5)
            };
        }

        public async Task<SessionInfo> UpdateSessionTitleAsync(string sessionId, string title, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                throw new InvalidOperationException("SessionId is required.");
            var trimmed = title?.Trim();
            if (string.IsNullOrEmpty(trimmed))
                throw new InvalidOperationException("Title is required.");

            await _dbLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await using var conn = CreateConnection();
                await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

                await using (var update = conn.CreateCommand())
                {
                    update.CommandText = "UPDATE sessions SET title = $title, updated_at = $updatedAt WHERE session_id = $sessionId;";
                    update.Parameters.AddWithValue("$title", trimmed);
                    update.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
                    update.Parameters.AddWithValue("$sessionId", sessionId.Trim());
                    var rows = await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    if (rows == 0)
                        throw new InvalidOperationException($"Session '{sessionId}' was not found.");
                }
            }
            finally
            {
                _dbLock.Release();
            }

            var refreshed = await GetSessionAsync(sessionId.Trim(), cancellationToken).ConfigureAwait(false);
            return refreshed ?? throw new InvalidOperationException($"Session '{sessionId}' was not found after update.");
        }

        public async Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                return;

            await _dbLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await using var conn = CreateConnection();
                await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
                await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

                // Explicit removal of every child row keeps behavior correct even if FK pragma is off.
                var tables = new[]
                {
                    "session_turns",
                    "session_trace_links",
                    "session_summaries",
                    "session_project_moves"
                };
                foreach (var table in tables)
                {
                    await using var del = conn.CreateCommand();
                    del.Transaction = tx;
                    del.CommandText = $"DELETE FROM {table} WHERE session_id = $sessionId;";
                    del.Parameters.AddWithValue("$sessionId", sessionId.Trim());
                    await del.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                await using (var delSession = conn.CreateCommand())
                {
                    delSession.Transaction = tx;
                    delSession.CommandText = "DELETE FROM sessions WHERE session_id = $sessionId;";
                    delSession.Parameters.AddWithValue("$sessionId", sessionId.Trim());
                    await delSession.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _dbLock.Release();
            }
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
SELECT session_id, title, project_id, created_at, updated_at, turn_count
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
                    ProjectId = reader.IsDBNull(2) ? null : reader.GetString(2),
                    CreatedAt = DateTimeOffset.Parse(reader.GetString(3)),
                    UpdatedAt = DateTimeOffset.Parse(reader.GetString(4)),
                    TurnCount = reader.GetInt32(5)
                });
            }

            return results;
        }

        public async Task<IReadOnlyList<SessionInfo>> ListSessionsByProjectAsync(string projectId, int limit = 50, int offset = 0, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(projectId))
                return Array.Empty<SessionInfo>();
            return await ListSessionsCoreAsync(
                "WHERE project_id = $projectId",
                configure: cmd => cmd.Parameters.AddWithValue("$projectId", projectId.Trim()),
                limit,
                offset,
                cancellationToken).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<SessionInfo>> ListStandaloneSessionsAsync(int limit = 50, int offset = 0, CancellationToken cancellationToken = default)
        {
            return await ListSessionsCoreAsync(
                "WHERE project_id IS NULL OR project_id = ''",
                configure: null,
                limit,
                offset,
                cancellationToken).ConfigureAwait(false);
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
SELECT turn_id, turn_group_id, session_id, sequence, role, content, agent_id, created_at, attachments_json
FROM (
  SELECT turn_id, turn_group_id, session_id, sequence, role, content, agent_id, created_at, attachments_json
  FROM session_turns
  WHERE session_id = $id
  ORDER BY sequence DESC
  LIMIT $lastTurns
)
ORDER BY sequence ASC;"
                : @"
SELECT turn_id, turn_group_id, session_id, sequence, role, content, agent_id, created_at, attachments_json
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
                turns.Add(ReadSessionTurn(reader));
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
            var hasAttachments = !string.IsNullOrWhiteSpace(turn.AttachmentsJson);
            if (string.IsNullOrWhiteSpace(turn.Content) && !hasAttachments)
            {
                throw new InvalidOperationException("Content or attachments are required.");
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
                    TurnGroupId = string.IsNullOrWhiteSpace(turn.TurnGroupId)
                        ? (string.IsNullOrWhiteSpace(turn.TurnId) ? Guid.NewGuid().ToString() : turn.TurnId)
                        : turn.TurnGroupId,
                    SessionId = turn.SessionId,
                    Sequence = nextSequence,
                    Role = turn.Role,
                    Content = string.IsNullOrWhiteSpace(turn.Content) && hasAttachments ? "" : turn.Content,
                    AgentId = string.IsNullOrWhiteSpace(turn.AgentId) ? null : turn.AgentId,
                    CreatedAt = turn.CreatedAt == default ? DateTimeOffset.UtcNow : turn.CreatedAt,
                    AttachmentsJson = turn.AttachmentsJson
                };

                await using (var insertCmd = conn.CreateCommand())
                {
                    insertCmd.CommandText = @"
INSERT INTO session_turns (turn_id, turn_group_id, session_id, sequence, role, content, agent_id, created_at, attachments_json)
VALUES ($turnId, $turnGroupId, $sessionId, $sequence, $role, $content, $agentId, $createdAt, $attachmentsJson);";
                    insertCmd.Parameters.AddWithValue("$turnId", normalized.TurnId);
                    insertCmd.Parameters.AddWithValue("$turnGroupId", normalized.TurnGroupId);
                    insertCmd.Parameters.AddWithValue("$sessionId", normalized.SessionId);
                    insertCmd.Parameters.AddWithValue("$sequence", normalized.Sequence);
                    insertCmd.Parameters.AddWithValue("$role", normalized.Role.ToString());
                    insertCmd.Parameters.AddWithValue("$content", normalized.Content);
                    insertCmd.Parameters.AddWithValue("$agentId", (object?)normalized.AgentId ?? DBNull.Value);
                    insertCmd.Parameters.AddWithValue("$createdAt", normalized.CreatedAt.ToString("O"));
                    insertCmd.Parameters.AddWithValue("$attachmentsJson", (object?)normalized.AttachmentsJson ?? DBNull.Value);
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

        public async Task<IReadOnlyList<SessionTraceLink>> GetTraceLinksAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return Array.Empty<SessionTraceLink>();
            }

            var links = new List<SessionTraceLink>();
            await using var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT trace_link_id, session_id, turn_group_id, request_turn_id, response_turn_id, primary_trace_id,
       request_trace_id, response_trace_id, agent_id, created_at, updated_at
FROM session_trace_links
WHERE session_id = $sessionId
ORDER BY created_at ASC;";
            cmd.Parameters.AddWithValue("$sessionId", sessionId);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                links.Add(ReadTraceLink(reader));
            }

            return links;
        }

        public async Task<SessionTraceLink?> GetTraceLinkByTurnIdAsync(string sessionId, string turnId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(turnId))
            {
                return null;
            }

            await using var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT trace_link_id, session_id, turn_group_id, request_turn_id, response_turn_id, primary_trace_id,
       request_trace_id, response_trace_id, agent_id, created_at, updated_at
FROM session_trace_links
WHERE session_id = $sessionId
  AND (request_turn_id = $turnId OR response_turn_id = $turnId)
LIMIT 1;";
            cmd.Parameters.AddWithValue("$sessionId", sessionId);
            cmd.Parameters.AddWithValue("$turnId", turnId);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return ReadTraceLink(reader);
        }

        public async Task<SessionTraceLink> UpsertTraceLinkAsync(SessionTraceLink traceLink, CancellationToken cancellationToken = default)
        {
            if (traceLink == null) throw new ArgumentNullException(nameof(traceLink));
            if (string.IsNullOrWhiteSpace(traceLink.SessionId))
            {
                throw new InvalidOperationException("TraceLink SessionId is required.");
            }
            if (string.IsNullOrWhiteSpace(traceLink.TurnGroupId))
            {
                throw new InvalidOperationException("TraceLink TurnGroupId is required.");
            }
            if (string.IsNullOrWhiteSpace(traceLink.RequestTurnId))
            {
                throw new InvalidOperationException("TraceLink RequestTurnId is required.");
            }

            await _dbLock.WaitAsync(cancellationToken);
            try
            {
                await EnsureSessionAsync(traceLink.SessionId, cancellationToken);
                await using var conn = CreateConnection();
                await conn.OpenAsync(cancellationToken);

                var normalized = new SessionTraceLink
                {
                    TraceLinkId = string.IsNullOrWhiteSpace(traceLink.TraceLinkId) ? Guid.NewGuid().ToString() : traceLink.TraceLinkId,
                    SessionId = traceLink.SessionId,
                    TurnGroupId = traceLink.TurnGroupId,
                    RequestTurnId = traceLink.RequestTurnId,
                    ResponseTurnId = string.IsNullOrWhiteSpace(traceLink.ResponseTurnId) ? null : traceLink.ResponseTurnId,
                    PrimaryTraceId = string.IsNullOrWhiteSpace(traceLink.PrimaryTraceId) ? null : traceLink.PrimaryTraceId,
                    RequestTraceId = string.IsNullOrWhiteSpace(traceLink.RequestTraceId) ? null : traceLink.RequestTraceId,
                    ResponseTraceId = string.IsNullOrWhiteSpace(traceLink.ResponseTraceId) ? null : traceLink.ResponseTraceId,
                    AgentId = string.IsNullOrWhiteSpace(traceLink.AgentId) ? null : traceLink.AgentId,
                    CreatedAt = traceLink.CreatedAt == default ? DateTimeOffset.UtcNow : traceLink.CreatedAt,
                    UpdatedAt = DateTimeOffset.UtcNow
                };

                await using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
INSERT INTO session_trace_links (
  trace_link_id, session_id, turn_group_id, request_turn_id, response_turn_id, primary_trace_id,
  request_trace_id, response_trace_id, agent_id, created_at, updated_at
)
VALUES (
  $traceLinkId, $sessionId, $turnGroupId, $requestTurnId, $responseTurnId, $primaryTraceId,
  $requestTraceId, $responseTraceId, $agentId, $createdAt, $updatedAt
)
ON CONFLICT(session_id, turn_group_id)
DO UPDATE SET
  request_turn_id = excluded.request_turn_id,
  response_turn_id = excluded.response_turn_id,
  primary_trace_id = excluded.primary_trace_id,
  request_trace_id = excluded.request_trace_id,
  response_trace_id = excluded.response_trace_id,
  agent_id = excluded.agent_id,
  updated_at = excluded.updated_at;";
                cmd.Parameters.AddWithValue("$traceLinkId", normalized.TraceLinkId);
                cmd.Parameters.AddWithValue("$sessionId", normalized.SessionId);
                cmd.Parameters.AddWithValue("$turnGroupId", normalized.TurnGroupId);
                cmd.Parameters.AddWithValue("$requestTurnId", normalized.RequestTurnId);
                cmd.Parameters.AddWithValue("$responseTurnId", (object?)normalized.ResponseTurnId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$primaryTraceId", (object?)normalized.PrimaryTraceId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$requestTraceId", (object?)normalized.RequestTraceId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$responseTraceId", (object?)normalized.ResponseTraceId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$agentId", (object?)normalized.AgentId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$createdAt", normalized.CreatedAt.ToString("O"));
                cmd.Parameters.AddWithValue("$updatedAt", normalized.UpdatedAt.ToString("O"));
                await cmd.ExecuteNonQueryAsync(cancellationToken);

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

        public async Task<SessionProject> CreateProjectAsync(
            string? projectId = null,
            string? name = null,
            string? scenarioId = null,
            string? focusEntityKey = null,
            string? focusDisplayName = null,
            CancellationToken cancellationToken = default)
        {
            var id = string.IsNullOrWhiteSpace(projectId) ? Guid.NewGuid().ToString() : projectId.Trim();
            var now = DateTimeOffset.UtcNow;
            var resolvedName = string.IsNullOrWhiteSpace(name) ? $"Project {now:yyyy-MM-dd HH:mm:ss}" : name.Trim();
            var resolvedScenarioId = string.IsNullOrWhiteSpace(scenarioId) ? SessionProjectTypes.People : scenarioId.Trim().ToLowerInvariant();
            var resolvedFocusKey = NormalizeFocusEntityKey(focusEntityKey);
            var resolvedFocusName = string.IsNullOrWhiteSpace(focusDisplayName) ? null : focusDisplayName.Trim();

            await _dbLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await using var conn = CreateConnection();
                await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
INSERT INTO session_projects (project_id, name, scenario_id, focus_entity_key, focus_display_name, settings_json, created_at, updated_at)
VALUES ($id, $name, $scenarioId, $focusKey, $focusName, $settingsJson, $createdAt, $updatedAt)
ON CONFLICT(project_id) DO UPDATE SET
  scenario_id = excluded.scenario_id,
  name = excluded.name,
  focus_entity_key = excluded.focus_entity_key,
  focus_display_name = excluded.focus_display_name,
  settings_json = excluded.settings_json,
  updated_at = excluded.updated_at;";
                cmd.Parameters.AddWithValue("$id", id);
                cmd.Parameters.AddWithValue("$name", resolvedName);
                cmd.Parameters.AddWithValue("$scenarioId", resolvedScenarioId);
                cmd.Parameters.AddWithValue("$focusKey", resolvedFocusKey is null ? DBNull.Value : resolvedFocusKey);
                cmd.Parameters.AddWithValue("$focusName", resolvedFocusName is null ? DBNull.Value : resolvedFocusName);
                cmd.Parameters.AddWithValue("$settingsJson", DBNull.Value);
                cmd.Parameters.AddWithValue("$createdAt", now.ToString("O"));
                cmd.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _dbLock.Release();
            }

            return await GetProjectAsync(id, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Failed to create project '{id}'.");
        }

        public async Task<SessionProject?> GetProjectAsync(string projectId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(projectId))
                return null;

            await using var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT p.project_id, p.name, p.scenario_id, p.focus_entity_key, p.focus_display_name, p.settings_json, p.created_at, p.updated_at, COALESCE(COUNT(s.session_id), 0)
FROM session_projects p
LEFT JOIN sessions s ON s.project_id = p.project_id
WHERE p.project_id = $id
GROUP BY p.project_id, p.name, p.scenario_id, p.focus_entity_key, p.focus_display_name, p.settings_json, p.created_at, p.updated_at;";
            cmd.Parameters.AddWithValue("$id", projectId.Trim());

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                return null;
            return ReadProject(reader);
        }

        public async Task<IReadOnlyList<SessionProject>> ListProjectsAsync(int limit = 50, int offset = 0, CancellationToken cancellationToken = default)
        {
            limit = limit <= 0 ? 50 : limit;
            offset = offset < 0 ? 0 : offset;
            var list = new List<SessionProject>();

            await using var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT p.project_id, p.name, p.scenario_id, p.focus_entity_key, p.focus_display_name, p.settings_json, p.created_at, p.updated_at, COALESCE(COUNT(s.session_id), 0)
FROM session_projects p
LEFT JOIN sessions s ON s.project_id = p.project_id
GROUP BY p.project_id, p.name, p.scenario_id, p.focus_entity_key, p.focus_display_name, p.settings_json, p.created_at, p.updated_at
ORDER BY p.updated_at DESC
LIMIT $limit OFFSET $offset;";
            cmd.Parameters.AddWithValue("$limit", limit);
            cmd.Parameters.AddWithValue("$offset", offset);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                list.Add(ReadProject(reader));
            return list;
        }

        public async Task<SessionProject> UpdateProjectAsync(SessionProject project, CancellationToken cancellationToken = default)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            if (string.IsNullOrWhiteSpace(project.ProjectId)) throw new InvalidOperationException("ProjectId is required.");

            var now = DateTimeOffset.UtcNow;
            await _dbLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await using var conn = CreateConnection();
                await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
UPDATE session_projects
SET name = $name, scenario_id = $scenarioId, focus_entity_key = $focusKey, focus_display_name = $focusName, settings_json = $settingsJson, updated_at = $updatedAt
WHERE project_id = $id;";
                cmd.Parameters.AddWithValue("$name", (project.Name ?? "").Trim());
                var scenarioId = string.IsNullOrWhiteSpace(project.ScenarioId) ? SessionProjectTypes.People : project.ScenarioId.Trim().ToLowerInvariant();
                cmd.Parameters.AddWithValue("$scenarioId", scenarioId);
                var focusKey = NormalizeFocusEntityKey(project.FocusEntityKey);
                cmd.Parameters.AddWithValue("$focusKey", focusKey is null ? DBNull.Value : focusKey);
                var focusName = string.IsNullOrWhiteSpace(project.FocusDisplayName) ? null : project.FocusDisplayName.Trim();
                cmd.Parameters.AddWithValue("$focusName", focusName is null ? DBNull.Value : focusName);
                cmd.Parameters.AddWithValue("$settingsJson", string.IsNullOrWhiteSpace(project.SettingsJson) ? DBNull.Value : project.SettingsJson);
                cmd.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
                cmd.Parameters.AddWithValue("$id", project.ProjectId.Trim());
                var changed = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                if (changed == 0)
                    throw new InvalidOperationException($"Project '{project.ProjectId}' not found.");
            }
            finally
            {
                _dbLock.Release();
            }

            return await GetProjectAsync(project.ProjectId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Project '{project.ProjectId}' not found.");
        }

        public async Task DeleteProjectAsync(string projectId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(projectId))
                return;

            await _dbLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await using var conn = CreateConnection();
                await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

                await using (var detach = conn.CreateCommand())
                {
                    detach.CommandText = "UPDATE sessions SET project_id = NULL, updated_at = $updatedAt WHERE project_id = $projectId;";
                    detach.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
                    detach.Parameters.AddWithValue("$projectId", projectId.Trim());
                    await detach.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                await using (var del = conn.CreateCommand())
                {
                    del.CommandText = "DELETE FROM session_projects WHERE project_id = $projectId;";
                    del.Parameters.AddWithValue("$projectId", projectId.Trim());
                    await del.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task AssignSessionToProjectAsync(string sessionId, string projectId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) throw new InvalidOperationException("SessionId is required.");
            if (string.IsNullOrWhiteSpace(projectId)) throw new InvalidOperationException("ProjectId is required.");

            await _dbLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await EnsureSessionAsync(sessionId.Trim(), cancellationToken).ConfigureAwait(false);
                await using var conn = CreateConnection();
                await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

                var fromProject = await GetSessionProjectIdAsync(conn, sessionId.Trim(), cancellationToken).ConfigureAwait(false);
                var exists = await ProjectExistsAsync(conn, projectId.Trim(), cancellationToken).ConfigureAwait(false);
                if (!exists)
                    throw new InvalidOperationException($"Project '{projectId}' not found.");

                await using (var update = conn.CreateCommand())
                {
                    update.CommandText = "UPDATE sessions SET project_id = $projectId, updated_at = $updatedAt WHERE session_id = $sessionId;";
                    update.Parameters.AddWithValue("$projectId", projectId.Trim());
                    update.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
                    update.Parameters.AddWithValue("$sessionId", sessionId.Trim());
                    await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                await AppendMoveAuditAsync(conn, sessionId.Trim(), fromProject, projectId.Trim(), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task DetachSessionFromProjectAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) throw new InvalidOperationException("SessionId is required.");

            await _dbLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await EnsureSessionAsync(sessionId.Trim(), cancellationToken).ConfigureAwait(false);
                await using var conn = CreateConnection();
                await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

                var fromProject = await GetSessionProjectIdAsync(conn, sessionId.Trim(), cancellationToken).ConfigureAwait(false);
                await using (var update = conn.CreateCommand())
                {
                    update.CommandText = "UPDATE sessions SET project_id = NULL, updated_at = $updatedAt WHERE session_id = $sessionId;";
                    update.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
                    update.Parameters.AddWithValue("$sessionId", sessionId.Trim());
                    await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
                await AppendMoveAuditAsync(conn, sessionId.Trim(), fromProject, null, cancellationToken).ConfigureAwait(false);
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
  project_id TEXT NULL,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  turn_count INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS session_turns (
  turn_id TEXT PRIMARY KEY,
  turn_group_id TEXT NULL,
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
);

CREATE TABLE IF NOT EXISTS session_trace_links (
  trace_link_id TEXT PRIMARY KEY,
  session_id TEXT NOT NULL,
  turn_group_id TEXT NOT NULL,
  request_turn_id TEXT NOT NULL,
  response_turn_id TEXT NULL,
  primary_trace_id TEXT NULL,
  request_trace_id TEXT NULL,
  response_trace_id TEXT NULL,
  agent_id TEXT NULL,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  FOREIGN KEY(session_id) REFERENCES sessions(session_id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_session_trace_links_session_group
ON session_trace_links(session_id, turn_group_id);

CREATE INDEX IF NOT EXISTS idx_session_trace_links_session_request_turn
ON session_trace_links(session_id, request_turn_id);

CREATE INDEX IF NOT EXISTS idx_session_trace_links_session_response_turn
ON session_trace_links(session_id, response_turn_id);

CREATE TABLE IF NOT EXISTS session_projects (
  project_id TEXT PRIMARY KEY,
  name TEXT NOT NULL,
  scenario_id TEXT NOT NULL,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS session_project_moves (
  move_id TEXT PRIMARY KEY,
  session_id TEXT NOT NULL,
  from_project_id TEXT NULL,
  to_project_id TEXT NULL,
  moved_at TEXT NOT NULL,
  FOREIGN KEY(session_id) REFERENCES sessions(session_id) ON DELETE CASCADE
);
;";
            cmd.ExecuteNonQuery();
            EnsureTurnGroupColumn(conn);
            EnsureAttachmentsJsonColumn(conn);
            // Index uses project_id — must run after legacy ALTER adds the column.
            EnsureSessionProjectColumn(conn);
            EnsureSessionProjectsSchema(conn);
            EnsureProjectFocusColumns(conn);
            EnsureProjectSettingsColumn(conn);
            using var projIdx = conn.CreateCommand();
            projIdx.CommandText = @"
CREATE INDEX IF NOT EXISTS idx_sessions_project_updated
ON sessions(project_id, updated_at DESC);";
            projIdx.ExecuteNonQuery();
            using var indexCmd = conn.CreateCommand();
            indexCmd.CommandText = @"
CREATE INDEX IF NOT EXISTS idx_session_turns_session_group
ON session_turns(session_id, turn_group_id);";
            indexCmd.ExecuteNonQuery();
        }

        private async Task EnsureSessionAsync(string sessionId, CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;
            await using var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT OR IGNORE INTO sessions (session_id, title, project_id, created_at, updated_at, turn_count)
VALUES ($id, $title, NULL, $createdAt, $updatedAt, 0);";
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

        private static SessionTraceLink ReadTraceLink(SqliteDataReader reader)
        {
            return new SessionTraceLink
            {
                TraceLinkId = reader.GetString(0),
                SessionId = reader.GetString(1),
                TurnGroupId = reader.GetString(2),
                RequestTurnId = reader.GetString(3),
                ResponseTurnId = reader.IsDBNull(4) ? null : reader.GetString(4),
                PrimaryTraceId = reader.IsDBNull(5) ? null : reader.GetString(5),
                RequestTraceId = reader.IsDBNull(6) ? null : reader.GetString(6),
                ResponseTraceId = reader.IsDBNull(7) ? null : reader.GetString(7),
                AgentId = reader.IsDBNull(8) ? null : reader.GetString(8),
                CreatedAt = DateTimeOffset.Parse(reader.GetString(9)),
                UpdatedAt = DateTimeOffset.Parse(reader.GetString(10))
            };
        }

        private static SessionProject ReadProject(SqliteDataReader reader)
        {
            var scenarioId = reader.IsDBNull(2) ? SessionProjectTypes.People : reader.GetString(2);
            var settingsJson = reader.IsDBNull(5) ? null : reader.GetString(5);
            var settings = ChatProjectSettings.FromJson(settingsJson);
            return new SessionProject
            {
                ProjectId = reader.GetString(0),
                Name = reader.GetString(1),
                ScenarioId = scenarioId,
                FocusEntityKey = reader.IsDBNull(3) ? null : reader.GetString(3),
                FocusDisplayName = reader.IsDBNull(4) ? null : reader.GetString(4),
                SettingsJson = settingsJson,
                VisualMaxPhotos = settings.VisualMaxPhotos,
                CreatedAt = DateTimeOffset.Parse(reader.GetString(6)),
                UpdatedAt = DateTimeOffset.Parse(reader.GetString(7)),
                SessionCount = reader.GetInt32(8)
            };
        }

        private static string? NormalizeFocusEntityKey(string? key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return null;
            return PersonaScenarioScope.SanitizeFolderSegment(key.Trim()).ToLowerInvariant();
        }

        private static void EnsureProjectFocusColumns(SqliteConnection conn)
        {
            using var pragma = conn.CreateCommand();
            pragma.CommandText = "PRAGMA table_info(session_projects);";
            var hasFocusKey = false;
            var hasFocusName = false;
            using (var reader = pragma.ExecuteReader())
            {
                while (reader.Read())
                {
                    var col = reader.GetString(1);
                    if (string.Equals(col, "focus_entity_key", StringComparison.OrdinalIgnoreCase)) hasFocusKey = true;
                    if (string.Equals(col, "focus_display_name", StringComparison.OrdinalIgnoreCase)) hasFocusName = true;
                }
            }

            if (!hasFocusKey)
            {
                using var alter = conn.CreateCommand();
                alter.CommandText = "ALTER TABLE session_projects ADD COLUMN focus_entity_key TEXT NULL;";
                alter.ExecuteNonQuery();
            }

            if (!hasFocusName)
            {
                using var alter = conn.CreateCommand();
                alter.CommandText = "ALTER TABLE session_projects ADD COLUMN focus_display_name TEXT NULL;";
                alter.ExecuteNonQuery();
            }
        }

        private static void EnsureProjectSettingsColumn(SqliteConnection conn)
        {
            using var pragma = conn.CreateCommand();
            pragma.CommandText = "PRAGMA table_info(session_projects);";
            var hasSettings = false;
            using (var reader = pragma.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (string.Equals(reader.GetString(1), "settings_json", StringComparison.OrdinalIgnoreCase))
                        hasSettings = true;
                }
            }

            if (!hasSettings)
            {
                using var alter = conn.CreateCommand();
                alter.CommandText = "ALTER TABLE session_projects ADD COLUMN settings_json TEXT NULL;";
                alter.ExecuteNonQuery();
            }
        }

        private static void EnsureTurnGroupColumn(SqliteConnection conn)
        {
            using var pragma = conn.CreateCommand();
            pragma.CommandText = "PRAGMA table_info(session_turns);";
            var hasTurnGroupId = false;
            {
                using var reader = pragma.ExecuteReader();
                while (reader.Read())
                {
                    if (string.Equals(reader.GetString(1), "turn_group_id", StringComparison.OrdinalIgnoreCase))
                    {
                        hasTurnGroupId = true;
                        break;
                    }
                }
            }

            if (!hasTurnGroupId)
            {
                using var alter = conn.CreateCommand();
                alter.CommandText = "ALTER TABLE session_turns ADD COLUMN turn_group_id TEXT NULL;";
                alter.ExecuteNonQuery();
            }

        }

        private static void EnsureAttachmentsJsonColumn(SqliteConnection conn)
        {
            using var pragma = conn.CreateCommand();
            pragma.CommandText = "PRAGMA table_info(session_turns);";
            var hasColumn = false;
            using (var reader = pragma.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (string.Equals(reader.GetString(1), "attachments_json", StringComparison.OrdinalIgnoreCase))
                    {
                        hasColumn = true;
                        break;
                    }
                }
            }

            if (!hasColumn)
            {
                using var alter = conn.CreateCommand();
                alter.CommandText = "ALTER TABLE session_turns ADD COLUMN attachments_json TEXT NULL;";
                alter.ExecuteNonQuery();
            }
        }

        private static SessionTurn ReadSessionTurn(Microsoft.Data.Sqlite.SqliteDataReader reader)
        {
            return new SessionTurn
            {
                TurnId = reader.GetString(0),
                TurnGroupId = reader.IsDBNull(1) ? reader.GetString(0) : reader.GetString(1),
                SessionId = reader.GetString(2),
                Sequence = reader.GetInt32(3),
                Role = ParseRole(reader.GetString(4)),
                Content = reader.GetString(5),
                AgentId = reader.IsDBNull(6) ? null : reader.GetString(6),
                CreatedAt = DateTimeOffset.Parse(reader.GetString(7)),
                AttachmentsJson = reader.FieldCount > 8 && !reader.IsDBNull(8) ? reader.GetString(8) : null
            };
        }

        private static void EnsureSessionProjectColumn(SqliteConnection conn)
        {
            using var pragma = conn.CreateCommand();
            pragma.CommandText = "PRAGMA table_info(sessions);";
            var hasProjectId = false;
            {
                using var reader = pragma.ExecuteReader();
                while (reader.Read())
                {
                    if (string.Equals(reader.GetString(1), "project_id", StringComparison.OrdinalIgnoreCase))
                    {
                        hasProjectId = true;
                        break;
                    }
                }
            }

            if (!hasProjectId)
            {
                using var alter = conn.CreateCommand();
                alter.CommandText = "ALTER TABLE sessions ADD COLUMN project_id TEXT NULL;";
                alter.ExecuteNonQuery();
            }
        }

        private static void EnsureSessionProjectsSchema(SqliteConnection conn)
        {
            using var pragma = conn.CreateCommand();
            pragma.CommandText = "PRAGMA table_info(session_projects);";
            var hasScenarioId = false;
            var hasProjectType = false;
            {
                using var reader = pragma.ExecuteReader();
                while (reader.Read())
                {
                    var col = reader.GetString(1);
                    if (string.Equals(col, "scenario_id", StringComparison.OrdinalIgnoreCase)) hasScenarioId = true;
                    if (string.Equals(col, "project_type", StringComparison.OrdinalIgnoreCase)) hasProjectType = true;
                }
            }

            if (!hasScenarioId)
            {
                using var alter = conn.CreateCommand();
                alter.CommandText = "ALTER TABLE session_projects ADD COLUMN scenario_id TEXT NULL;";
                alter.ExecuteNonQuery();

                using var backfill = conn.CreateCommand();
                backfill.CommandText = hasProjectType
                    ? "UPDATE session_projects SET scenario_id = COALESCE(NULLIF(project_type,''), 'people') WHERE scenario_id IS NULL OR scenario_id = '';"
                    : "UPDATE session_projects SET scenario_id = 'people' WHERE scenario_id IS NULL OR scenario_id = '';";
                backfill.ExecuteNonQuery();
            }

            if (hasProjectType)
            {
                // One-time migration: drop legacy project_type column by table rebuild.
                using var migrate = conn.CreateCommand();
                migrate.CommandText = @"
CREATE TABLE IF NOT EXISTS session_projects_v2 (
  project_id TEXT PRIMARY KEY,
  name TEXT NOT NULL,
  scenario_id TEXT NOT NULL,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);
INSERT INTO session_projects_v2 (project_id, name, scenario_id, created_at, updated_at)
SELECT project_id, name, COALESCE(NULLIF(scenario_id,''), NULLIF(project_type,''), 'people'), created_at, updated_at
FROM session_projects;
DROP TABLE session_projects;
ALTER TABLE session_projects_v2 RENAME TO session_projects;";
                migrate.ExecuteNonQuery();
            }
        }

        private async Task<IReadOnlyList<SessionInfo>> ListSessionsCoreAsync(
            string whereClause,
            Action<SqliteCommand>? configure,
            int limit,
            int offset,
            CancellationToken cancellationToken)
        {
            limit = limit <= 0 ? 50 : limit;
            offset = offset < 0 ? 0 : offset;
            var results = new List<SessionInfo>();

            await using var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
SELECT session_id, title, project_id, created_at, updated_at, turn_count
FROM sessions
{whereClause}
ORDER BY updated_at DESC
LIMIT $limit OFFSET $offset;";
            cmd.Parameters.AddWithValue("$limit", limit);
            cmd.Parameters.AddWithValue("$offset", offset);
            configure?.Invoke(cmd);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                results.Add(new SessionInfo
                {
                    SessionId = reader.GetString(0),
                    Title = reader.GetString(1),
                    ProjectId = reader.IsDBNull(2) ? null : reader.GetString(2),
                    CreatedAt = DateTimeOffset.Parse(reader.GetString(3)),
                    UpdatedAt = DateTimeOffset.Parse(reader.GetString(4)),
                    TurnCount = reader.GetInt32(5)
                });
            }

            return results;
        }

        private static async Task<bool> ProjectExistsAsync(SqliteConnection conn, string projectId, CancellationToken cancellationToken)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM session_projects WHERE project_id = $id LIMIT 1;";
            cmd.Parameters.AddWithValue("$id", projectId);
            var raw = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return raw != null && raw != DBNull.Value;
        }

        private static async Task<string?> GetSessionProjectIdAsync(SqliteConnection conn, string sessionId, CancellationToken cancellationToken)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT project_id FROM sessions WHERE session_id = $sessionId LIMIT 1;";
            cmd.Parameters.AddWithValue("$sessionId", sessionId);
            var raw = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (raw == null || raw == DBNull.Value)
                return null;
            var value = Convert.ToString(raw);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static async Task AppendMoveAuditAsync(
            SqliteConnection conn,
            string sessionId,
            string? fromProjectId,
            string? toProjectId,
            CancellationToken cancellationToken)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO session_project_moves (move_id, session_id, from_project_id, to_project_id, moved_at)
VALUES ($moveId, $sessionId, $fromProjectId, $toProjectId, $movedAt);";
            cmd.Parameters.AddWithValue("$moveId", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("$sessionId", sessionId);
            cmd.Parameters.AddWithValue("$fromProjectId", (object?)fromProjectId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$toProjectId", (object?)toProjectId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$movedAt", DateTimeOffset.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
