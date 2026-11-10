using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;

namespace AgctorSDK.Core.Workflows;

/// <summary>Small durable control record. Large artifacts stay with the tool/provider.</summary>
public sealed class AgentRun
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RequestId { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public string ScenarioId { get; set; } = "";
    public string Workflow { get; set; } = "";
    public string WorkspaceId { get; set; } = "";
    public string Status { get; set; } = "queued";
    public string Phase { get; set; } = "plan";
    public string Summary { get; set; } = "Waiting for the director.";
    public long Revision { get; set; }
    public int Steps { get; set; }
    public int Failures { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset DueAt { get; set; } = DateTimeOffset.UtcNow;
    public AgentQuestion? Question { get; set; }
    public AgentReply? Reply { get; set; }
    public JsonElement State { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AgentActiveCall>? ActiveCalls { get; set; }
    public bool IsTerminal => Status is "completed" or "cancelled" or "failed";
}

public sealed record AgentChoice(string Id, string Label);
public sealed record AgentQuestion(string Id, string Kind, string Prompt, IReadOnlyList<AgentChoice> Choices,
    bool AllowFreeText = true);
public sealed record AgentReply(string QuestionId, string? Choice, string? Text);
public sealed record AgentMessageRoute(string From, string To, string Phase, string ExchangeId,
    string FromKind = "agent", string ToKind = "agent", string? FromDetail = null, string? ToDetail = null,
    string? Operation = null, string? Outcome = null, long? DurationMs = null);
public sealed record AgentGraphNode(string Id, string Kind, string? Detail);
public sealed record AgentGraphEdge(string From, string To, string? Operation, string? Outcome, long Count, DateTimeOffset LastAt);
public sealed record AgentActiveCall(string ExchangeId, string From, string To, string Operation, DateTimeOffset StartedAt, DateTimeOffset ConfirmedUntil, string FromKind, string ToKind);
public sealed record AgentRunGraph(IReadOnlyList<AgentGraphNode> Nodes, IReadOnlyList<AgentGraphEdge> Edges, IReadOnlyList<AgentActiveCall>? ActiveCalls = null);
public sealed record AgentRunEvent(long Sequence, string RunId, DateTimeOffset At, string Agent, string Kind, string Message, AgentMessageRoute? Route = null);
public sealed record AgentRunLease(AgentRun Run, string Token);
/// <summary>Opt-in read-only monitoring for a workflow's specific human-wait states.</summary>
public sealed record AgentWaitMonitor(string Workflow, string Phase, string QuestionKind);

public interface IAgentRunStore
{
    Task<AgentRun> CreateAsync(AgentRun run, CancellationToken ct = default);
    Task<AgentRun?> GetAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<AgentRun>> ListAsync(string? scenarioId = null, CancellationToken ct = default);
    Task<AgentRunLease?> ClaimAsync(TimeSpan lifetime, CancellationToken ct = default, IReadOnlyList<AgentWaitMonitor>? monitors = null);
    Task<bool> RenewAsync(AgentRunLease lease, TimeSpan lifetime, CancellationToken ct = default);
    Task SaveAsync(AgentRunLease lease, string agent, string kind, string message, CancellationToken ct = default, AgentMessageRoute? route = null);
    Task ActivityAsync(AgentRunLease lease, string agent, string message, CancellationToken ct = default, AgentMessageRoute? route = null);
    Task<AgentRun> ReplyAsync(string id, long revision, AgentReply reply, CancellationToken ct = default);
    Task<AgentRun> CancelAsync(string id, CancellationToken ct = default);
    Task<AgentRunGraph> GraphAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<AgentRunEvent>> EventsAsync(string id, long after = 0, CancellationToken ct = default);
}
