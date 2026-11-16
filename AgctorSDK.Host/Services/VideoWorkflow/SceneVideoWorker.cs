using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Workflows;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Host.Services.VideoWorkflow;

/// <summary>One durable claim. All role mailboxes are run-scoped (Phase C).</summary>
public sealed record SceneVideoTick(AgentRunLease Lease);
public sealed record SceneVideoTickResult(string RunId);

/// <summary>
/// Run-scoped Director: only dispatches to the owning specialist or applies Director-owned decisions.
/// Holds no authoritative run state — SQLite checkpoints remain the source of truth.
/// </summary>
public sealed class SceneVideoDirectorActor(string id, string runId, IActorRuntimeAdapter runtime, SceneVideoLoop loop)
    : BaseActor(id, SceneVideoRoles.Director)
{
    public override async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (envelope.Payload is not SceneVideoTick tick) throw new ArgumentException("Expected a workflow tick.");
        var owner = SceneVideoRoles.Owner(tick.Lease.Run);
        // Director never executes another role's StepAsync body (no Role() hat-switching).
        if (owner == SceneVideoRoles.Director)
        {
            await loop.StepAsync(tick.Lease, cancellationToken);
            return new MessageEnvelope(new SceneVideoTickResult(tick.Lease.Run.Id));
        }
        var target = SceneVideoWorker.ActorId(runId, owner);
        var result = await runtime.SendMessageAsync<SceneVideoTickResult>(target, tick,
            TimeSpan.FromMinutes(8), senderId: Id, cancellationToken: cancellationToken);
        return new MessageEnvelope(result);
    }
}

/// <summary>Run-scoped specialist mailbox for one role; only that role's StepAsync runs here.</summary>
public sealed class SceneVideoRoleActor(string id, string role, SceneVideoLoop loop) : BaseActor(id, role)
{
    public override async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (envelope.Payload is not SceneVideoTick tick) throw new ArgumentException("Expected a workflow tick.");
        // Guard: refuse work owned by another mailbox (isolation).
        var owner = SceneVideoRoles.Owner(tick.Lease.Run);
        if (!string.Equals(owner, role, StringComparison.Ordinal))
            throw new InvalidOperationException($"{role} mailbox refused work owned by {owner}.");
        await loop.StepAsync(tick.Lease, cancellationToken);
        return new MessageEnvelope(new SceneVideoTickResult(tick.Lease.Run.Id));
    }
}

public sealed class SceneVideoWorker(IAgentRunStore store, SceneVideoLoop loop, IActorRuntimeAdapter runtime,
    IOptionsMonitor<SceneVideoOptions> options, ILogger<SceneVideoWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (options.CurrentValue.WorkerEnabled && runtime.IsInitialized)
                {
                    var lease = await store.ClaimAsync(TimeSpan.FromSeconds(60), stoppingToken, SceneVideoLoop.WaitMonitors);
                    if (lease != null) { await ExecuteLeaseAsync(lease, stoppingToken); continue; }
                }
                await Task.Delay(500, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Scene video worker will recover its durable checkpoint after the lease expires");
                await Task.Delay(1000, stoppingToken);
            }
        }
    }

    public async Task ExecuteLeaseAsync(AgentRunLease lease, CancellationToken ct)
    {
        using var step = CancellationTokenSource.CreateLinkedTokenSource(ct);
        step.CancelAfter(TimeSpan.FromMinutes(9));
        using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(step.Token);
        var claimedRevision = lease.Run.Revision;
        var renewal = RenewAsync();
        var director = ActorId(lease.Run.Id, SceneVideoRoles.Director);
        try
        {
            await EnsureRunActorsAsync(lease.Run.Id, step.Token);
            await runtime.SendMessageAsync<SceneVideoTickResult>(director, new SceneVideoTick(lease),
                TimeSpan.FromMinutes(9), cancellationToken: step.Token);
        }
        finally
        {
            heartbeat.Cancel();
            try { await renewal; } catch (OperationCanceledException) { }
            if (lease.Run.IsTerminal)
                await StopRunActorsAsync(lease.Run.Id);
        }

        async Task RenewAsync()
        {
            while (!heartbeat.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), heartbeat.Token);
                if (!await store.RenewAsync(lease, TimeSpan.FromSeconds(60), heartbeat.Token))
                {
                    if (lease.Run.Revision == claimedRevision) await step.CancelAsync();
                    return;
                }
            }
        }
    }

    /// <summary>Stable run-scoped actor id for a role (safe for mailbox keys).</summary>
    public static string ActorId(string runId, string role) => role switch
    {
        SceneVideoRoles.Director => "scene-director-" + runId,
        SceneVideoRoles.AudioEngineer => "scene-audio-" + runId,
        SceneVideoRoles.Planner => "scene-planner-" + runId,
        SceneVideoRoles.Project => "scene-project-" + runId,
        SceneVideoRoles.Memory => "scene-memory-" + runId,
        SceneVideoRoles.Render => "scene-render-" + runId,
        SceneVideoRoles.VisualCritic => "scene-visual-" + runId,
        SceneVideoRoles.Editor => "scene-editor-" + runId,
        _ => "scene-role-" + role.Replace(' ', '-').ToLowerInvariant() + "-" + runId
    };

    // Phase B helpers kept as aliases for tests / call sites.
    internal static string DirectorId(string runId) => ActorId(runId, SceneVideoRoles.Director);
    internal static string AudioId(string runId) => ActorId(runId, SceneVideoRoles.AudioEngineer);

    private async Task EnsureRunActorsAsync(string runId, CancellationToken ct)
    {
        foreach (var role in SceneVideoRoles.Agents)
        {
            var id = ActorId(runId, role);
            if (role == SceneVideoRoles.Director)
            {
                if (await runtime.GetActorAsync<SceneVideoDirectorActor>(id, ct) != null) continue;
                try { await runtime.RegisterActorAsync(new SceneVideoDirectorActor(id, runId, runtime, loop), ct); }
                catch (InvalidOperationException)
                {
                    if (await runtime.GetActorAsync<SceneVideoDirectorActor>(id, ct) == null) throw;
                }
                continue;
            }
            if (await runtime.GetActorAsync<SceneVideoRoleActor>(id, ct) != null) continue;
            try { await runtime.RegisterActorAsync(new SceneVideoRoleActor(id, role, loop), ct); }
            catch (InvalidOperationException)
            {
                if (await runtime.GetActorAsync<SceneVideoRoleActor>(id, ct) == null) throw;
            }
        }
    }

    private async Task StopRunActorsAsync(string runId)
    {
        foreach (var role in SceneVideoRoles.Agents)
            await runtime.StopActorAsync(ActorId(runId, role), CancellationToken.None);
    }
}
