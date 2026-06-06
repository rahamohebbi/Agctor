using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.ProjectMemory.Scenarios;
using AgctorSDK.Core.ProjectMemory.Scenarios.Actors;
using AgctorSDK.Core.ProjectMemory.Scenarios.Messages;
using AgctorSDK.Host.Models;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Host.Services.Scenarios;

/// <summary>PRD-024 actor-backed facade for multi-turn scenario flow runs.</summary>
public interface IScenarioFlowRuntimeOrchestrator
{
    Task<ScenarioFlowRuntimeResult> RunAsync(
        string scenarioId,
        ScenarioDefinition definition,
        ScenarioFlowRunRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Resume a flow suspended at <c>AwaitEvent</c> (PRD-024 Phase C).</summary>
    Task<ScenarioFlowRuntimeResult> ResumeDomainEventAsync(
        string scenarioId,
        ScenarioDefinition definition,
        string projectRoot,
        string sessionId,
        string eventType,
        IReadOnlyDictionary<string, object?> payload,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// When photos were extracted but the flow stayed on <c>WaitForInput</c>, re-run resume with stored attachment ids.
    /// </summary>
    Task<ScenarioFlowRuntimeResult?> TryAdvanceStuckPhotoCollectionAsync(
        string scenarioId,
        ScenarioDefinition definition,
        string projectRoot,
        string sessionId,
        CancellationToken cancellationToken = default);
}

public sealed class ScenarioFlowRuntimeOrchestrator : IScenarioFlowRuntimeOrchestrator
{
    private const string SenderId = "scenario-flow-orchestrator";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(10);

    private readonly IActorRuntimeAdapter _runtime;
    private readonly IScenarioFlowRuntimeStore _store;
    private readonly IScenarioFlowSegmentExecutor _segmentExecutor;
    private readonly IOptionsMonitor<ScenarioFlowHostOptions> _flowOptions;
    private readonly SemaphoreSlim _spawnLock = new(1, 1);

    public ScenarioFlowRuntimeOrchestrator(
        IActorRuntimeAdapter runtime,
        IScenarioFlowRuntimeStore store,
        IScenarioFlowSegmentExecutor segmentExecutor,
        IOptionsMonitor<ScenarioFlowHostOptions> flowOptions)
    {
        _runtime = runtime;
        _store = store;
        _segmentExecutor = segmentExecutor;
        _flowOptions = flowOptions;
    }

    public async Task<ScenarioFlowRuntimeResult> RunAsync(
        string scenarioId,
        ScenarioDefinition definition,
        ScenarioFlowRunRequest request,
        CancellationToken cancellationToken = default)
    {
        var flow = definition.Flow ?? throw new InvalidOperationException("Scenario has no flow.");
        var sessionId = request.SessionId?.Trim() ?? Guid.NewGuid().ToString("N");
        var projectRoot = request.ProjectRoot?.Trim() ?? throw new InvalidOperationException("Project root is required.");
        var flowJson = JsonSerializer.Serialize(flow, ScenarioFlowJson.Options);
        var correlationId = Guid.NewGuid().ToString("N");
        var attachments = (IReadOnlyList<string>)(request.AttachmentIds ?? new List<string>());

        var existing = await _store.LoadAsync(projectRoot, sessionId, scenarioId, cancellationToken).ConfigureAwait(false);
        object payload;
        if (existing == null || existing.Status == ScenarioFlowRuntimeStatus.Completed || existing.Status == ScenarioFlowRuntimeStatus.Failed || existing.Status == ScenarioFlowRuntimeStatus.Idle)
        {
            payload = new ScenarioFlowStartMessage(
                sessionId,
                scenarioId,
                projectRoot,
                flow.GraphId,
                flowJson,
                request.Message?.Trim() ?? string.Empty,
                attachments,
                correlationId);
        }
        else if (existing.Status == ScenarioFlowRuntimeStatus.WaitingForUserInput)
        {
            payload = new ScenarioFlowResumeUserInputMessage(
                sessionId,
                scenarioId,
                projectRoot,
                flow.GraphId,
                flowJson,
                request.Message?.Trim() ?? string.Empty,
                attachments,
                correlationId);
        }
        else if (existing.Status == ScenarioFlowRuntimeStatus.WaitingForDomainEvent)
        {
            // Allow attachment-only turns while extract runs; playground publishes the domain event next.
            if (attachments.Count > 0 || !string.IsNullOrWhiteSpace(request.Message))
            {
                var interim = ScenarioFlowInterimText.ForSnapshot(existing)
                                ?? ScenarioFlowInterimText.SuspendFallback(existing.Status);
                return new ScenarioFlowRuntimeResult(
                    true,
                    false,
                    existing.Status,
                    existing.ExecutionNodeId,
                    interim,
                    interim,
                    null);
            }

            return new ScenarioFlowRuntimeResult(
                false,
                false,
                existing.Status,
                existing.ExecutionNodeId,
                null,
                existing.PendingPrompt,
                $"Flow is waiting for domain event '{existing.AwaitingEvent?.EventType}'.");
        }
        else
        {
            return new ScenarioFlowRuntimeResult(
                false,
                false,
                existing.Status,
                existing.ExecutionNodeId,
                null,
                existing.PendingPrompt,
                $"Flow is in status '{existing.Status}' and cannot accept this message.");
        }

        var actorId = ScenarioFlowCapabilities.RuntimeActorId(sessionId, scenarioId);
        await EnsureRuntimeActorAsync(actorId, cancellationToken).ConfigureAwait(false);

        return await SendAsync<ScenarioFlowRuntimeResult>(
            actorId,
            payload,
            correlationId,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ScenarioFlowRuntimeResult> ResumeDomainEventAsync(
        string scenarioId,
        ScenarioDefinition definition,
        string projectRoot,
        string sessionId,
        string eventType,
        IReadOnlyDictionary<string, object?> payload,
        CancellationToken cancellationToken = default)
    {
        var flow = definition.Flow ?? throw new InvalidOperationException("Scenario has no flow.");
        var flowJson = JsonSerializer.Serialize(flow, ScenarioFlowJson.Options);
        var correlationId = Guid.NewGuid().ToString("N");
        var message = new ScenarioFlowResumeDomainEventMessage(
            sessionId,
            scenarioId,
            projectRoot,
            flow.GraphId,
            flowJson,
            eventType,
            payload,
            correlationId);

        var actorId = ScenarioFlowCapabilities.RuntimeActorId(sessionId, scenarioId);
        await EnsureRuntimeActorAsync(actorId, cancellationToken).ConfigureAwait(false);

        return await SendAsync<ScenarioFlowRuntimeResult>(
            actorId,
            message,
            correlationId,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ScenarioFlowRuntimeResult?> TryAdvanceStuckPhotoCollectionAsync(
        string scenarioId,
        ScenarioDefinition definition,
        string projectRoot,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await _store
            .LoadAsync(projectRoot, sessionId, scenarioId, cancellationToken)
            .ConfigureAwait(false);
        if (snapshot == null
            || snapshot.Status != ScenarioFlowRuntimeStatus.WaitingForUserInput
            || string.IsNullOrWhiteSpace(snapshot.ExecutionNodeId)
            || snapshot.Store.Attachments.AllInRun.Count == 0)
        {
            return null;
        }

        var flowJson = JsonSerializer.Serialize(definition.Flow ?? throw new InvalidOperationException("Scenario has no flow."), ScenarioFlowJson.Options);
        if (!ScenarioFlowWaitForInputHelper.AcceptsAttachments(flowJson, snapshot.ExecutionNodeId))
            return null;

        return await RunAsync(
            scenarioId,
            definition,
            new ScenarioFlowRunRequest
            {
                ProjectRoot = projectRoot,
                SessionId = sessionId,
                Message = string.Empty,
                AttachmentIds = snapshot.Store.Attachments.AllInRun.ToList()
            },
            cancellationToken).ConfigureAwait(false);
    }

    private TimeSpan ResolveLlmNodeTimeout(int? requestSeconds)
    {
        var sec = requestSeconds ?? _flowOptions.CurrentValue.LlmNodeTimeoutSeconds;
        if (sec <= 0)
            return Timeout.InfiniteTimeSpan;
        return TimeSpan.FromSeconds(Math.Clamp(sec, 5, 3600));
    }

    private async Task EnsureRuntimeActorAsync(string actorId, CancellationToken cancellationToken)
    {
        if (await _runtime.GetActorAsync<ScenarioFlowRuntimeActor>(actorId, cancellationToken).ConfigureAwait(false) != null)
            return;

        await _spawnLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (await _runtime.GetActorAsync<ScenarioFlowRuntimeActor>(actorId, cancellationToken).ConfigureAwait(false) == null)
            {
                var llmTimeout = ResolveLlmNodeTimeout(null);
                await _runtime.SpawnActorAsync(
                    actorId,
                    id => new ScenarioFlowRuntimeActor(id, _store, _segmentExecutor, llmTimeout),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _spawnLock.Release();
        }
    }

    private Task<TResponse> SendAsync<TResponse>(
        string actorId,
        object payload,
        string correlationId,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        var headers = new Dictionary<string, string>
        {
            [AgctorMessageHeaders.MessageType] = payload.GetType().Name,
            [AgctorMessageHeaders.CorrelationId] = correlationId
        };

        return _runtime.SendMessageAsync<TResponse>(
            actorId,
            payload,
            RequestTimeout,
            SenderId,
            headers,
            cancellationToken);
    }
}
