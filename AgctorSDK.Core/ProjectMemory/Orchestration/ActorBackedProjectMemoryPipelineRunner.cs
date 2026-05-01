using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.ProjectMemory.Orchestration.Actors;
using AgctorSDK.Core.ProjectMemory.OutOfSchema;

namespace AgctorSDK.Core.ProjectMemory.Orchestration;

/// <summary>
/// PRD-020 facade that exposes the existing pipeline interface while routing
/// work through actor mailboxes. The wrapped runner remains the compatibility
/// implementation until each workflow step is fully actor-owned.
/// </summary>
public sealed class ActorBackedProjectMemoryPipelineRunner : IProjectMemoryPipelineRunner
{
    private const string SenderId = "project-memory-pipeline-facade";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(10);

    private readonly IActorRuntimeAdapter _runtime;
    private readonly IProjectMemoryPipelineRunner _directRunner;
    private readonly SemaphoreSlim _spawnLock = new(1, 1);

    public ActorBackedProjectMemoryPipelineRunner(
        IActorRuntimeAdapter runtime,
        IProjectMemoryPipelineRunner directRunner)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _directRunner = directRunner ?? throw new ArgumentNullException(nameof(directRunner));
    }

    public async Task<ProjectMemoryPipelineResult> RunAsync(
        ProjectMemoryPipelineRequest request,
        CancellationToken cancellationToken = default)
    {
        var actorId = await EnsureWorkflowActorAsync(cancellationToken).ConfigureAwait(false);
        var result = await SendAsync<ProjectMemoryWorkflowResult>(
            actorId,
            new ProjectMemoryWorkflowRequest(request),
            request.CorrelationId,
            cancellationToken).ConfigureAwait(false);

        return result.PipelineResult;
    }

    public async Task<ProjectMemoryIngestResult> IngestFromExtractorOutputAsync(
        string projectRoot,
        string? scenarioId,
        string rawExtractorLlmText,
        CancellationToken cancellationToken = default)
    {
        var actorId = await EnsureIngestActorAsync(cancellationToken).ConfigureAwait(false);
        var result = await SendAsync<ProjectMemoryIngestWorkflowResult>(
            actorId,
            new ProjectMemoryIngestWorkflowRequest(projectRoot, scenarioId, rawExtractorLlmText),
            correlationId: null,
            cancellationToken).ConfigureAwait(false);

        return result.IngestResult;
    }

    public async Task<GenericInboxPersistResult> PersistApprovedGenericFactsAsync(
        string projectRoot,
        string? scenarioId,
        IReadOnlyList<ApprovedGenericFact> approvals,
        CancellationToken cancellationToken = default)
    {
        var actorId = await EnsureGenericInboxActorAsync(cancellationToken).ConfigureAwait(false);
        var result = await SendAsync<ProjectMemoryGenericInboxPersistResult>(
            actorId,
            new ProjectMemoryGenericInboxPersistRequest(projectRoot, scenarioId, approvals),
            correlationId: null,
            cancellationToken).ConfigureAwait(false);

        return result.PersistResult;
    }

    private async Task<TResponse> SendAsync<TResponse>(
        string actorId,
        object payload,
        string? correlationId,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        var headers = new Dictionary<string, string>
        {
            [AgctorMessageHeaders.MessageType] = payload.GetType().Name
        };
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            headers[AgctorMessageHeaders.CorrelationId] = correlationId;
        }

        return await _runtime.SendMessageAsync<TResponse>(
            actorId,
            payload,
            RequestTimeout,
            SenderId,
            headers,
            cancellationToken).ConfigureAwait(false);
    }

    private Task<string> EnsureWorkflowActorAsync(CancellationToken cancellationToken) =>
        EnsureActorAsync(
            "project-memory:workflow",
            id => new ProjectMemoryWorkflowActor(id, _directRunner),
            cancellationToken);

    private Task<string> EnsureIngestActorAsync(CancellationToken cancellationToken) =>
        EnsureActorAsync(
            "project-memory:ingest",
            id => new ProjectMemoryIngestActor(id, _directRunner),
            cancellationToken);

    private Task<string> EnsureGenericInboxActorAsync(CancellationToken cancellationToken) =>
        EnsureActorAsync(
            "project-memory:generic-inbox",
            id => new ProjectMemoryGenericInboxActor(id, _directRunner),
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

