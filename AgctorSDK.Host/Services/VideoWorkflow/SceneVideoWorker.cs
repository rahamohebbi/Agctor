using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Workflows;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Host.Services.VideoWorkflow;

public sealed record SceneVideoTick(AgentRunLease Lease);
public sealed record SceneVideoTickResult(string RunId);

/// <summary>The director routes each durable step to a specialist mailbox; actors hold no authoritative run state.</summary>
public sealed class SceneVideoDirectorActor(string id, string specialistId, IActorRuntimeAdapter runtime)
    : BaseActor(id, "SceneVideoDirector")
{
    public override async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        var result = await runtime.SendMessageAsync<SceneVideoTickResult>(specialistId, envelope.Payload,
            TimeSpan.FromMinutes(8), senderId: Id, cancellationToken: cancellationToken);
        return new MessageEnvelope(result);
    }
}
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
        var renewal = RenewAsync();
        var director = "scene-director-" + lease.Token; var specialist = "scene-specialist-" + lease.Token;
        try
        {
            await runtime.RegisterActorAsync(new SceneVideoSpecialistActor(specialist, SceneVideoLoop.Role(lease.Run), loop), step.Token);
            await runtime.RegisterActorAsync(new SceneVideoDirectorActor(director, specialist, runtime), step.Token);
            await runtime.SendMessageAsync<SceneVideoTickResult>(director, new SceneVideoTick(lease), TimeSpan.FromMinutes(9), cancellationToken: step.Token);
        }
        finally
        {
            heartbeat.Cancel();
            try { await renewal; } catch (OperationCanceledException) { }
            await runtime.StopActorAsync(director, CancellationToken.None);
            await runtime.StopActorAsync(specialist, CancellationToken.None);
        }
        async Task RenewAsync()
        {
            while (!heartbeat.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), heartbeat.Token);
                if (!await store.RenewAsync(lease, TimeSpan.FromSeconds(60), heartbeat.Token))
                { await step.CancelAsync(); return; }
            }
        }
    }
}
