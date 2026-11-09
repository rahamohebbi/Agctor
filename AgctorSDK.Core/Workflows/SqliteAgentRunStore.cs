using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AgctorSDK.Core.Workflows;

/// <summary>Transactional snapshots + ordered events, optimistic revisions and renewable work leases.</summary>
public sealed class SqliteAgentRunStore : IAgentRunStore
{
    private readonly string _connection;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public SqliteAgentRunStore(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _connection = new SqliteConnectionStringBuilder { DataSource = path, DefaultTimeout = 10 }.ToString();
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS agent_runs (
              id TEXT PRIMARY KEY, request_id TEXT NOT NULL UNIQUE, request_hash TEXT NOT NULL,
              workspace TEXT NOT NULL, scenario TEXT NOT NULL, status TEXT NOT NULL,
              revision INTEGER NOT NULL, due INTEGER NOT NULL, lease TEXT, lease_until INTEGER,
              body TEXT NOT NULL);
            CREATE UNIQUE INDEX IF NOT EXISTS agent_run_workspace ON agent_runs(workspace)
              WHERE status IN ('queued','running','waiting');
            CREATE INDEX IF NOT EXISTS agent_run_due ON agent_runs(status,due);
            CREATE TABLE IF NOT EXISTS agent_run_events (
              seq INTEGER PRIMARY KEY AUTOINCREMENT, run_id TEXT NOT NULL, at TEXT NOT NULL,
              agent TEXT NOT NULL, kind TEXT NOT NULL, message TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS agent_run_event_lookup ON agent_run_events(run_id,seq);
            CREATE TABLE IF NOT EXISTS agent_run_event_routes (
              event_seq INTEGER PRIMARY KEY REFERENCES agent_run_events(seq), body TEXT NOT NULL);
            """;
        cmd.ExecuteNonQuery();
    }
    private SqliteConnection Open() { var db = new SqliteConnection(_connection); db.Open(); return db; }
    private static SqliteCommand Command(SqliteConnection db, SqliteTransaction? tx, string sql, params (string, object?)[] values)
    {
        var cmd = db.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = sql;
        foreach (var (key, value) in values) cmd.Parameters.AddWithValue(key, value ?? DBNull.Value);
        return cmd;
    }
    private static AgentRun? Read(SqliteConnection db, SqliteTransaction? tx, string key, bool request = false)
    {
        using var cmd = Command(db, tx, "SELECT body FROM agent_runs WHERE " + (request ? "request_id" : "id") + "=$id", ("$id", key));
        return cmd.ExecuteScalar() is string json ? JsonSerializer.Deserialize<AgentRun>(json, Json) : null;
    }
    private static void Event(SqliteConnection db, SqliteTransaction tx, string id, string agent, string kind, string message, AgentMessageRoute? route = null)
    {
        using var cmd = Command(db, tx, "INSERT INTO agent_run_events(run_id,at,agent,kind,message) VALUES($id,$at,$agent,$kind,$message)",
            ("$id", id), ("$at", DateTimeOffset.UtcNow.ToString("O")), ("$agent", agent), ("$kind", kind), ("$message", message));
        cmd.ExecuteNonQuery();
        if (route != null) {
            using var routing = Command(db, tx, "INSERT INTO agent_run_event_routes(event_seq,body) VALUES(last_insert_rowid(),$body)", ("$body", JsonSerializer.Serialize(route, Json)));
            routing.ExecuteNonQuery();
        }
    }
    public Task<AgentRun> CreateAsync(AgentRun run, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); using var db = Open(); using var tx = db.BeginTransaction();
        var existing = Read(db, tx, run.RequestId, request: true);
        if (existing != null)
        {
            if (existing.RequestHash != run.RequestHash) throw new InvalidOperationException("This request ID was already used for different inputs.");
            return Task.FromResult(existing);
        }
        using var cmd = Command(db, tx, """
            INSERT INTO agent_runs(id,request_id,request_hash,workspace,scenario,status,revision,due,body)
            VALUES($id,$request,$hash,$workspace,$scenario,$status,0,$due,$body)
            """, ("$id", run.Id), ("$request", run.RequestId), ("$hash", run.RequestHash), ("$workspace", run.WorkspaceId),
            ("$scenario", run.ScenarioId), ("$status", run.Status), ("$due", run.DueAt.ToUnixTimeMilliseconds()), ("$body", JsonSerializer.Serialize(run, Json)));
        try { cmd.ExecuteNonQuery(); }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        { throw new InvalidOperationException("This Scene Memory workspace already has an active run. Finish or cancel it before starting another."); }
        Event(db, tx, run.Id, "Director", "queued", run.Summary); tx.Commit(); return Task.FromResult(run);
    }
    public Task<AgentRun?> GetAsync(string id, CancellationToken ct = default)
    { ct.ThrowIfCancellationRequested(); using var db = Open(); return Task.FromResult(Read(db, null, id)); }
    public Task<IReadOnlyList<AgentRun>> ListAsync(string? scenarioId = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); using var db = Open();
        using var cmd = Command(db, null, "SELECT body FROM agent_runs WHERE ($s IS NULL OR scenario=$s) ORDER BY rowid DESC LIMIT 100", ("$s", scenarioId));
        using var reader = cmd.ExecuteReader(); var rows = new List<AgentRun>();
        while (reader.Read()) rows.Add(JsonSerializer.Deserialize<AgentRun>(reader.GetString(0), Json)!);
        return Task.FromResult<IReadOnlyList<AgentRun>>(rows);
    }
    public Task<AgentRunLease?> ClaimAsync(TimeSpan lifetime, CancellationToken ct = default, IReadOnlyList<AgentWaitMonitor>? monitors = null)
    {
        ct.ThrowIfCancellationRequested(); using var db = Open(); using var tx = db.BeginTransaction();
        var now = DateTimeOffset.UtcNow;
        using var select = Command(db, tx, """
            SELECT id FROM agent_runs WHERE
              (status IN ('queued','running') OR (status='waiting' AND EXISTS (
                SELECT 1 FROM json_each($monitors) m
                WHERE json_extract(body,'$.workflow')=json_extract(m.value,'$.workflow')
                  AND json_extract(body,'$.phase')=json_extract(m.value,'$.phase')
                  AND json_extract(body,'$.question.kind')=json_extract(m.value,'$.questionKind'))))
              AND due<=$now AND (lease IS NULL OR lease_until<=$now) ORDER BY due LIMIT 1
            """, ("$now", now.ToUnixTimeMilliseconds()), ("$monitors", JsonSerializer.Serialize(monitors ?? [], Json)));
        if (select.ExecuteScalar() is not string id) return Task.FromResult<AgentRunLease?>(null);
        var token = Guid.NewGuid().ToString("N");
        using var claim = Command(db, tx, "UPDATE agent_runs SET lease=$lease,lease_until=$until WHERE id=$id",
            ("$lease", token), ("$until", (now + lifetime).ToUnixTimeMilliseconds()), ("$id", id));
        claim.ExecuteNonQuery(); var run = Read(db, tx, id)!; tx.Commit(); return Task.FromResult<AgentRunLease?>(new(run, token));
    }
    public Task<bool> RenewAsync(AgentRunLease lease, TimeSpan lifetime, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); using var db = Open();
        using var cmd = Command(db, null, "UPDATE agent_runs SET lease_until=$until WHERE id=$id AND lease=$lease AND revision=$revision AND status IN ('queued','running','waiting') AND lease_until>$now",
            ("$until", (DateTimeOffset.UtcNow + lifetime).ToUnixTimeMilliseconds()), ("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
            ("$id", lease.Run.Id), ("$lease", lease.Token), ("$revision", lease.Run.Revision));
        return Task.FromResult(cmd.ExecuteNonQuery() == 1);
    }
    public Task SaveAsync(AgentRunLease lease, string agent, string kind, string message, CancellationToken ct = default, AgentMessageRoute? route = null)
    {
        ct.ThrowIfCancellationRequested(); using var db = Open(); using var tx = db.BeginTransaction();
        var run = lease.Run; var previous = run.Revision;
        var changed = run.Summary != message;
        // Heartbeats persist freshness without flooding activity history. Lease/revision checks still protect human replies.
        run.Revision++; run.UpdatedAt = DateTimeOffset.UtcNow; run.Summary = message;
        using var cmd = Command(db, tx, """
            UPDATE agent_runs SET workspace=$workspace,status=$status,revision=$next,due=$due,body=$body,lease=NULL,lease_until=NULL
            WHERE id=$id AND revision=$old AND lease=$lease AND lease_until>$now
            """, ("$workspace", run.WorkspaceId), ("$status", run.Status), ("$next", run.Revision), ("$due", run.DueAt.ToUnixTimeMilliseconds()),
            ("$body", JsonSerializer.Serialize(run, Json)), ("$id", run.Id), ("$old", previous), ("$lease", lease.Token), ("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("The run changed or its work lease expired. This worker cannot overwrite it.");
        if (kind != "observation" || changed || route != null) Event(db, tx, run.Id, agent, kind, message, route);
        if (kind != "observation" && run.Question != null && run.Status == "waiting")
            Event(db, tx, run.Id, "Director", "question", run.Question.Prompt, new("Director", "Human", run.Phase, route?.ExchangeId ?? Guid.NewGuid().ToString("N")));
        tx.Commit(); return Task.CompletedTask;
    }
    public Task ActivityAsync(AgentRunLease lease, string agent, string message, CancellationToken ct = default, AgentMessageRoute? route = null)
    {
        ct.ThrowIfCancellationRequested(); using var db = Open(); using var tx = db.BeginTransaction();
        using var cmd = Command(db, tx, "SELECT COUNT(*) FROM agent_runs WHERE id=$id AND lease=$lease AND lease_until>$now AND revision=$revision",
            ("$id", lease.Run.Id), ("$lease", lease.Token), ("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), ("$revision", lease.Run.Revision));
        if ((long)cmd.ExecuteScalar()! != 1) throw new OperationCanceledException("The run no longer belongs to this worker.");
        Event(db, tx, lease.Run.Id, agent, "activity", message, route); tx.Commit(); return Task.CompletedTask;
    }
    public Task<AgentRun> ReplyAsync(string id, long revision, AgentReply reply, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); using var db = Open(); using var tx = db.BeginTransaction();
        var run = Read(db, tx, id) ?? throw new KeyNotFoundException("Run not found.");
        if (run.Revision != revision || run.Status != "waiting" || run.Question?.Id != reply.QuestionId)
            throw new InvalidOperationException("This question has changed or was already answered. Refresh the run.");
        if ((!string.IsNullOrEmpty(reply.Choice) && !run.Question.Choices.Any(c => c.Id == reply.Choice)) ||
            (string.IsNullOrWhiteSpace(reply.Choice) && string.IsNullOrWhiteSpace(reply.Text)) || reply.Text?.Length > 4000 ||
            (!run.Question.AllowFreeText && !string.IsNullOrWhiteSpace(reply.Text)))
            throw new ArgumentException("Choose an offered answer or enter up to 4,000 characters.");
        run.Reply = reply; run.Status = "queued"; run.Revision++; run.DueAt = DateTimeOffset.UtcNow;
        run.UpdatedAt = DateTimeOffset.UtcNow; run.Summary = "Answer received. The director will resume the task.";
        WriteInteractive(db, tx, run); Event(db, tx, id, "Human", "answered", run.Summary, new("Human", "Director", run.Phase, reply.QuestionId)); tx.Commit(); return Task.FromResult(run);
    }
    public Task<AgentRun> CancelAsync(string id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); using var db = Open(); using var tx = db.BeginTransaction();
        var run = Read(db, tx, id) ?? throw new KeyNotFoundException("Run not found.");
        if (!run.IsTerminal)
        {
            run.Status = "cancelled"; run.Question = null; run.Revision++; run.UpdatedAt = DateTimeOffset.UtcNow;
            run.Summary = "Run cancelled. Already submitted provider jobs may still finish; no further clips will be submitted.";
            WriteInteractive(db, tx, run); Event(db, tx, id, "Director", "cancelled", run.Summary);
        }
        tx.Commit(); return Task.FromResult(run);
    }
    private static void WriteInteractive(SqliteConnection db, SqliteTransaction tx, AgentRun run)
    {
        using var cmd = Command(db, tx, "UPDATE agent_runs SET body=$body,status=$status,revision=$revision,due=$due,lease=NULL,lease_until=NULL WHERE id=$id",
            ("$body", JsonSerializer.Serialize(run, Json)), ("$status", run.Status), ("$revision", run.Revision), ("$due", run.DueAt.ToUnixTimeMilliseconds()), ("$id", run.Id));
        cmd.ExecuteNonQuery();
    }
    public Task<AgentRunGraph> GraphAsync(string id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); using var db = Open();
        using var cmd = Command(db, null, """
            WITH connections AS (
              SELECT MAX(r.event_seq) AS seq, COUNT(*) AS total
              FROM agent_run_event_routes r JOIN agent_run_events e ON e.seq=r.event_seq
              WHERE e.run_id=$id
              GROUP BY json_extract(r.body,'$.from'),json_extract(r.body,'$.to'),json_extract(r.body,'$.operation'))
            SELECT r.body,e.at,c.total FROM connections c
              JOIN agent_run_event_routes r ON r.event_seq=c.seq JOIN agent_run_events e ON e.seq=c.seq
              ORDER BY c.seq
            """, ("$id", id));
        using var reader = cmd.ExecuteReader();
        var nodes = new Dictionary<string, AgentGraphNode>(); var edges = new List<AgentGraphEdge>();
        while (reader.Read()) {
            var route = JsonSerializer.Deserialize<AgentMessageRoute>(reader.GetString(0), Json)!;
            nodes[route.From] = new(route.From, route.From == "Human" ? "human" : route.FromKind, route.FromDetail ?? nodes.GetValueOrDefault(route.From)?.Detail);
            nodes[route.To] = new(route.To, route.To == "Human" ? "human" : route.ToKind, route.ToDetail ?? nodes.GetValueOrDefault(route.To)?.Detail);
            edges.Add(new(route.From, route.To, route.Operation, route.Outcome, reader.GetInt64(2), DateTimeOffset.Parse(reader.GetString(1))));
        }
        return Task.FromResult(new AgentRunGraph(nodes.Values.ToArray(), edges));
    }
    public Task<IReadOnlyList<AgentRunEvent>> EventsAsync(string id, long after = 0, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); using var db = Open();
        using var cmd = Command(db, null, "SELECT e.seq,e.at,e.agent,e.kind,e.message,r.body FROM agent_run_events e LEFT JOIN agent_run_event_routes r ON r.event_seq=e.seq WHERE e.run_id=$id AND e.seq>$after ORDER BY e.seq LIMIT 200", ("$id", id), ("$after", after));
        using var reader = cmd.ExecuteReader(); var events = new List<AgentRunEvent>();
        while (reader.Read()) events.Add(new(reader.GetInt64(0), id, DateTimeOffset.Parse(reader.GetString(1)), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.IsDBNull(5) ? null : JsonSerializer.Deserialize<AgentMessageRoute>(reader.GetString(5), Json)));
        return Task.FromResult<IReadOnlyList<AgentRunEvent>>(events);
    }
}
