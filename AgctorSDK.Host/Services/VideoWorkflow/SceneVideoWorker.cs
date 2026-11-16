using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Workflows;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Host.Services.VideoWorkflow;

/// <summary>One durable claim. SpecialistId is per-tick; Director/Audio ids are run-scoped.</summary>
public sealed record SceneVideoTick(AgentRunLease Lease, string SpecialistId);
public sealed record SceneVideoTickResult(string RunId);

/// <summary>
/// Run-scoped Director: routes to Audio Engineer or a legacy specialist; applies Human / audio decisions itself.
/// Holds no authoritative run state — SQLite checkpoints remain the source of truth.
/// </summary>
public sealed class SceneVideoDirectorActor(string id, string audioId, IActorRuntimeAdapter runtime, SceneVideoLoop loop)
    : BaseActor(id, SceneVideoRoles.Director)
{
    public override async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (envelope.Payload is not SceneVideoTick tick) throw new ArgumentException("Expected a workflow tick.");
        var run = tick.Lease.Run;
        // Audio Engineer owns audio/speech/narration work and continuity speech observation.
        if (SceneVideoRoles.NeedsAudioWaitingObserver(run) ||
            (run.Reply == null && SceneVideoRoles.OwnsAudioWork(run.Phase)))
        {
            var audio = await runtime.SendMessageAsync<SceneVideoTickResult>(audioId, tick,
                TimeSpan.FromMinutes(8), senderId: Id, cancellationToken: cancellationToken);
            return new MessageEnvelope(audio);
        }
        // Director applies Human replies and asks for audio decisions; never runs specialist tool phases here.
        if (run.Reply != null || SceneVideoRoles.OwnsDirectorAudioAsk(run.Phase) ||
            SceneVideoLoop.Role(run) == SceneVideoRoles.Director)
        {
            await loop.StepAsync(tick.Lease, cancellationToken);
            return new MessageEnvelope(new SceneVideoTickResult(run.Id));
        }
        // Remaining roles stay on a per-tick specialist mailbox until Phase C.
        var result = await runtime.SendMessageAsync<SceneVideoTickResult>(tick.SpecialistId, tick,
            TimeSpan.FromMinutes(8), senderId: Id, cancellationToken: cancellationToken);
        return new MessageEnvelope(result);
    }
}

/// <summary>Run-scoped Audio Engineer: owns audio/speech/narration phases and audio tools only.</summary>
public sealed class SceneVideoAudioEngineerActor(string id, SceneVideoLoop loop)
    : BaseActor(id, SceneVideoRoles.AudioEngineer)
{
    public override async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (envelope.Payload is not SceneVideoTick tick) throw new ArgumentException("Expected a workflow tick.");
        await loop.StepAsync(tick.Lease, cancellationToken);
        return new MessageEnvelope(new SceneVideoTickResult(tick.Lease.Run.Id));
    }
}

/// <summary>Per-tick specialist for non–Director / non–Audio Engineer roles until Phase C splits them.</summary>
public sealed class SceneVideoSpecialistActor(string id, string role, SceneVideoLoop loop) : BaseActor(id, role)
{
    public override async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (envelope.Payload is not SceneVideoTick tick) throw new ArgumentException("Expected a workflow tick.");
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
        // Run-scoped ids: Director + Audio Engineer mailboxes last for the run lifetime (locked Phase B decision).
        var director = DirectorId(lease.Run.Id);
        var audio = AudioId(lease.Run.Id);
        var specialist = "scene-specialist-" + lease.Token;
        var registeredSpecialist = false;
        try
        {
            await EnsureRunActorsAsync(director, audio, step.Token);
            await runtime.RegisterActorAsync(new SceneVideoSpecialistActor(specialist, SceneVideoLoop.Role(lease.Run), loop), step.Token);
            registeredSpecialist = true;
            await runtime.SendMessageAsync<SceneVideoTickResult>(director, new SceneVideoTick(lease, specialist),
                TimeSpan.FromMinutes(9), cancellationToken: step.Token);
        }
        finally
        {
            heartbeat.Cancel();
            try { await renewal; } catch (OperationCanceledException) { }
            if (registeredSpecialist)
                await runtime.StopActorAsync(specialist, CancellationToken.None);
            // Stop run-scoped mailboxes only when the durable run is finished.
            if (lease.Run.IsTerminal)
            {
                await runtime.StopActorAsync(director, CancellationToken.None);
                await runtime.StopActorAsync(audio, CancellationToken.None);
            }
        }

        async Task RenewAsync()
        {
            while (!heartbeat.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), heartbeat.Token);
                if (!await store.RenewAsync(lease, TimeSpan.FromSeconds(60), heartbeat.Token))
                {
                    // SaveAsync advances this lease's revision and releases it before
                    // the actor reply arrives. That completed checkpoint is not lost work.
                    if (lease.Run.Revision == claimedRevision) await step.CancelAsync();
                    return;
                }
            }
        }
    }

    internal static string DirectorId(string runId) => "scene-director-" + runId;
    internal static string AudioId(string runId) => "scene-audio-" + runId;

    private async Task EnsureRunActorsAsync(string director, string audio, CancellationToken ct)
    {
        if (await runtime.GetActorAsync<SceneVideoAudioEngineerActor>(audio, ct) == null)
        {
            try { await runtime.RegisterActorAsync(new SceneVideoAudioEngineerActor(audio, loop), ct); }
            catch (InvalidOperationException)
            {
                if (await runtime.GetActorAsync<SceneVideoAudioEngineerActor>(audio, ct) == null) throw;
            }
        }
        if (await runtime.GetActorAsync<SceneVideoDirectorActor>(director, ct) == null)
        {
            try { await runtime.RegisterActorAsync(new SceneVideoDirectorActor(director, audio, runtime, loop), ct); }
            catch (InvalidOperationException)
            {
                if (await runtime.GetActorAsync<SceneVideoDirectorActor>(director, ct) == null) throw;
            }
        }
    }
}
