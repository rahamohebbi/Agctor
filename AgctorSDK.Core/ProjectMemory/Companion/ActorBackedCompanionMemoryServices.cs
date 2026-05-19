using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.ProjectMemory.Companion.Actors;
using AgctorSDK.Core.ProjectMemory.LifeSignals;
using AgctorSDK.Core.ProjectMemory.Orchestration;

namespace AgctorSDK.Core.ProjectMemory.Companion;

/// <summary>
/// PRD-021 facade: routes companion automation through dedicated actor mailboxes.
/// </summary>
public sealed class ActorBackedCompanionMemoryServices : ISessionEndIngestService, IProactiveSignalsService
{
    private const string SenderId = "companion-memory-facade";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(10);

    private readonly IActorRuntimeAdapter _runtime;
    private readonly ISessionStore _sessions;
    private readonly IProjectMemoryPipelineRunner _pipeline;
    private readonly SemaphoreSlim _spawnLock = new(1, 1);

    public ActorBackedCompanionMemoryServices(
        IActorRuntimeAdapter runtime,
        ISessionStore sessions,
        IProjectMemoryPipelineRunner pipeline)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public async Task<SessionEndIngestResult> TryIngestOnSessionEndAsync(
        string sessionId,
        string projectRoot,
        SessionEndIngestTrigger trigger,
        CancellationToken cancellationToken = default)
    {
        var actorId = await EnsureSessionEndIngestActorAsync(cancellationToken).ConfigureAwait(false);
        var workflow = await SendAsync<SessionEndIngestWorkflowResult>(
            actorId,
            new SessionEndIngestWorkflowRequest(sessionId, projectRoot, null, trigger),
            cancellationToken).ConfigureAwait(false);

        return new SessionEndIngestResult(
            workflow.Success,
            workflow.Skipped,
            workflow.SkipReason,
            workflow.CorrelationId,
            workflow.FinalTextSnippet,
            workflow.LastIncludedSequence);
    }

    public async Task<IReadOnlyList<PersonLifeSignal>> ScanAsync(
        string projectRoot,
        string? scenarioId,
        int staleContactDays = 30,
        int birthdayHorizonDays = 14,
        CancellationToken cancellationToken = default)
    {
        var actorId = await EnsureProactiveSignalsActorAsync(cancellationToken).ConfigureAwait(false);
        var workflow = await SendAsync<ProactiveSignalsWorkflowResult>(
            actorId,
            new ProactiveSignalsWorkflowRequest(projectRoot, scenarioId, staleContactDays, birthdayHorizonDays),
            cancellationToken).ConfigureAwait(false);

        return workflow.Signals;
    }

    private async Task<TResponse> SendAsync<TResponse>(
        string actorId,
        object payload,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        var headers = new Dictionary<string, string>
        {
            [AgctorMessageHeaders.MessageType] = payload.GetType().Name
        };

        return await _runtime.SendMessageAsync<TResponse>(
            actorId,
            payload,
            RequestTimeout,
            SenderId,
            headers,
            cancellationToken).ConfigureAwait(false);
    }

    private Task<string> EnsureSessionEndIngestActorAsync(CancellationToken cancellationToken) =>
        EnsureActorAsync(
            "companion:session-end-ingest",
            id => new SessionEndIngestActor(id, _sessions, _pipeline),
            cancellationToken);

    private Task<string> EnsureProactiveSignalsActorAsync(CancellationToken cancellationToken) =>
        EnsureActorAsync(
            "companion:proactive-signals",
            id => new ProactiveSignalsActor(id),
            cancellationToken);

    private async Task<string> EnsureActorAsync<TActor>(
        string actorId,
        Func<string, TActor> factory,
        CancellationToken cancellationToken)
        where TActor : class, IActor
    {
        if (await _runtime.GetActorAsync<TActor>(actorId, cancellationToken).ConfigureAwait(false) != null)
            return actorId;

        await _spawnLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (await _runtime.GetActorAsync<TActor>(actorId, cancellationToken).ConfigureAwait(false) == null)
            {
                await _runtime.SpawnActorAsync(actorId, factory, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _spawnLock.Release();
        }

        return actorId;
    }
}
