using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.ProjectMemory.Scenarios.Messages;

namespace AgctorSDK.Core.ProjectMemory.Scenarios.Actors;

/// <summary>
/// PRD-024: owns multi-turn scenario flow state for one session + scenario pair.
/// Graph segments are delegated to <see cref="IScenarioFlowSegmentExecutor"/> (Host).
/// </summary>
public sealed class ScenarioFlowRuntimeActor : IActor
{
    private readonly IScenarioFlowRuntimeStore _store;
    private readonly IScenarioFlowSegmentExecutor _segmentExecutor;
    private readonly TimeSpan _llmNodeTimeout;

    public ScenarioFlowRuntimeActor(
        string id,
        IScenarioFlowRuntimeStore store,
        IScenarioFlowSegmentExecutor segmentExecutor,
        TimeSpan? llmNodeTimeout = null)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("Actor id is required.", nameof(id)) : id;
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _segmentExecutor = segmentExecutor ?? throw new ArgumentNullException(nameof(segmentExecutor));
        _llmNodeTimeout = llmNodeTimeout is { } t && t > TimeSpan.Zero
            ? t
            : TimeSpan.FromMinutes(10);
    }

    public string Id { get; }

    public string ActorType => nameof(ScenarioFlowRuntimeActor);

    public ActorState State => _state;

    private ActorState _state = ActorState.Initializing;

    public event EventHandler<ActorStateChangedEventArgs>? StateChanged;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ChangeState(ActorState.Active, "Initialized");
        return Task.CompletedTask;
    }

    public async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        try
        {
            return envelope.Payload switch
            {
                ScenarioFlowStartMessage start => AgctorEnvelopeBuilder.Response(
                    await HandleStartAsync(start, cancellationToken).ConfigureAwait(false),
                    envelope,
                    Id,
                    AgctorMessageTypes.Result),
                ScenarioFlowResumeUserInputMessage resume => AgctorEnvelopeBuilder.Response(
                    await HandleResumeUserInputAsync(resume, cancellationToken).ConfigureAwait(false),
                    envelope,
                    Id,
                    AgctorMessageTypes.Result),
                ScenarioFlowResumeDomainEventMessage domain => AgctorEnvelopeBuilder.Response(
                    await HandleResumeDomainEventAsync(domain, cancellationToken).ConfigureAwait(false),
                    envelope,
                    Id,
                    AgctorMessageTypes.Result),
                ScenarioFlowCancelMessage cancel => AgctorEnvelopeBuilder.Response(
                    await HandleCancelAsync(cancel, cancellationToken).ConfigureAwait(false),
                    envelope,
                    Id,
                    AgctorMessageTypes.Result),
                _ => AgctorEnvelopeBuilder.Error(
                    envelope,
                    Id,
                    $"Unsupported scenario flow payload '{envelope.Payload?.GetType().Name ?? "null"}'.")
            };
        }
        catch (Exception ex)
        {
            return AgctorEnvelopeBuilder.Error(envelope, Id, ex.Message, ex);
        }
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        ChangeState(ActorState.Stopped, "Shutdown");
        return Task.CompletedTask;
    }

    private async Task<ScenarioFlowRuntimeResult> HandleStartAsync(
        ScenarioFlowStartMessage message,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new ScenarioFlowRuntimeSnapshot
        {
            FlowId = message.FlowId,
            ExecutionNodeId = string.Empty,
            Status = ScenarioFlowRuntimeStatus.Running,
            StartedAtUtc = now,
            UpdatedAtUtc = now
        };

        ScenarioFlowLoopTraversal.MergeAttachmentDelta(snapshot, message.AttachmentIds);
        return await RunSegmentAndPersistAsync(message, snapshot, message.UserMessage, message.AttachmentIds, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ScenarioFlowRuntimeResult> HandleResumeUserInputAsync(
        ScenarioFlowResumeUserInputMessage message,
        CancellationToken cancellationToken)
    {
        var snapshot = await _store.LoadAsync(message.ProjectRoot, message.SessionId, message.ScenarioId, cancellationToken)
            .ConfigureAwait(false);
        if (snapshot == null)
            return FailResult("No active flow run to resume.", string.Empty, ScenarioFlowRuntimeStatus.Failed);

        if (snapshot.Status != ScenarioFlowRuntimeStatus.WaitingForUserInput)
            return FailResult($"Cannot resume user input while status is {snapshot.Status}.", snapshot.ExecutionNodeId, snapshot.Status);

        ScenarioFlowLoopTraversal.MergeAttachmentDelta(snapshot, message.AttachmentIds);
        snapshot.Status = ScenarioFlowRuntimeStatus.Running;
        snapshot.PendingPrompt = null;

        // Resume sets status Running, so the segment runner would re-enter WaitForInput unless we
        // advance to the loopBack target (e.g. n_ask → n_visual) when photos arrive this turn.
        if (message.AttachmentIds.Count > 0
            && ScenarioFlowWaitForInputHelper.AcceptsAttachments(message.FlowJson, snapshot.ExecutionNodeId))
        {
            snapshot.ExecutionNodeId = ScenarioFlowGraphNavigation.ResolveResumeTargetNode(
                message.FlowJson,
                snapshot.ExecutionNodeId);
        }

        return await RunSegmentAndPersistAsync(message, snapshot, message.UserMessage, message.AttachmentIds, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ScenarioFlowRuntimeResult> HandleResumeDomainEventAsync(
        ScenarioFlowResumeDomainEventMessage message,
        CancellationToken cancellationToken)
    {
        var snapshot = await _store.LoadAsync(message.ProjectRoot, message.SessionId, message.ScenarioId, cancellationToken)
            .ConfigureAwait(false);
        if (snapshot == null)
            return FailResult("No active flow run to resume.", string.Empty, ScenarioFlowRuntimeStatus.Failed);

        if (snapshot.Status != ScenarioFlowRuntimeStatus.WaitingForDomainEvent)
            return FailResult($"Cannot resume domain event while status is {snapshot.Status}.", snapshot.ExecutionNodeId, snapshot.Status);

        if (snapshot.AwaitingEvent != null
            && !string.Equals(snapshot.AwaitingEvent.EventType, message.EventType, StringComparison.OrdinalIgnoreCase))
        {
            return FailResult(
                $"Expected event '{snapshot.AwaitingEvent.EventType}' but received '{message.EventType}'.",
                snapshot.ExecutionNodeId,
                snapshot.Status);
        }

        foreach (var (key, value) in message.Payload)
            snapshot.Store.Facts[key] = value;

        snapshot.Status = ScenarioFlowRuntimeStatus.Running;
        snapshot.AwaitingEvent = null;
        // Running status skips ResolveStartNodeId's WaitingForDomainEvent branch — advance past AwaitEvent first.
        snapshot.ExecutionNodeId = ScenarioFlowGraphNavigation.ResolveResumeTargetNode(
            message.FlowJson,
            snapshot.ExecutionNodeId);

        var userMessage = ScenarioFlowRuntimePrompts.ResolveOriginalUserMessage(snapshot, message.FlowJson);
        if (string.Equals(message.EventType, ScenarioFlowDomainEventTypes.VisualExtractCompleted, StringComparison.OrdinalIgnoreCase))
        {
            userMessage = ScenarioFlowRuntimePrompts.BuildPostExtractStyleUserMessage(snapshot, message.FlowJson);
        }

        return await RunSegmentAndPersistAsync(message, snapshot, userMessage, Array.Empty<string>(), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ScenarioFlowRuntimeResult> HandleCancelAsync(
        ScenarioFlowCancelMessage message,
        CancellationToken cancellationToken)
    {
        await _store.DeleteAsync(message.ProjectRoot, message.SessionId, message.ScenarioId, cancellationToken)
            .ConfigureAwait(false);

        return new ScenarioFlowRuntimeResult(
            Success: true,
            Completed: false,
            Status: ScenarioFlowRuntimeStatus.Idle,
            ExecutionNodeId: string.Empty,
            Output: null,
            PendingPrompt: null,
            ErrorMessage: message.Reason);
    }

    private async Task<ScenarioFlowRuntimeResult> RunSegmentAndPersistAsync(
        ScenarioFlowStartMessage message,
        ScenarioFlowRuntimeSnapshot snapshot,
        string userMessage,
        IReadOnlyList<string> attachmentIds,
        CancellationToken cancellationToken) =>
        await RunSegmentAndPersistAsync(
            message.ProjectRoot,
            message.SessionId,
            message.ScenarioId,
            message.FlowId,
            message.FlowJson,
            snapshot,
            userMessage,
            attachmentIds,
            cancellationToken).ConfigureAwait(false);

    private Task<ScenarioFlowRuntimeResult> RunSegmentAndPersistAsync(
        ScenarioFlowResumeUserInputMessage message,
        ScenarioFlowRuntimeSnapshot snapshot,
        string userMessage,
        IReadOnlyList<string> attachmentIds,
        CancellationToken cancellationToken) =>
        RunSegmentAndPersistAsync(
            message.ProjectRoot,
            message.SessionId,
            message.ScenarioId,
            message.FlowId,
            message.FlowJson,
            snapshot,
            userMessage,
            attachmentIds,
            cancellationToken);

    private Task<ScenarioFlowRuntimeResult> RunSegmentAndPersistAsync(
        ScenarioFlowResumeDomainEventMessage message,
        ScenarioFlowRuntimeSnapshot snapshot,
        string userMessage,
        IReadOnlyList<string> attachmentIds,
        CancellationToken cancellationToken) =>
        RunSegmentAndPersistAsync(
            message.ProjectRoot,
            message.SessionId,
            message.ScenarioId,
            message.FlowId,
            message.FlowJson,
            snapshot,
            userMessage,
            attachmentIds,
            cancellationToken);

    private async Task<ScenarioFlowRuntimeResult> RunSegmentAndPersistAsync(
        string projectRoot,
        string sessionId,
        string scenarioId,
        string flowId,
        string flowJson,
        ScenarioFlowRuntimeSnapshot snapshot,
        string userMessage,
        IReadOnlyList<string> attachmentIds,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(flowJson))
            return FailResult("Flow graph JSON is required.", snapshot.ExecutionNodeId, ScenarioFlowRuntimeStatus.Failed);

        var segmentRequest = new ScenarioFlowSegmentRequest
        {
            ProjectRoot = projectRoot,
            ScenarioId = scenarioId,
            SessionId = sessionId,
            UserMessage = userMessage,
            AttachmentIds = attachmentIds,
            Snapshot = snapshot,
            FlowJson = flowJson,
            LlmNodeTimeout = _llmNodeTimeout
        };

        var segmentResult = await _segmentExecutor.RunSegmentAsync(segmentRequest, cancellationToken).ConfigureAwait(false);
        var updated = segmentResult.Snapshot;
        updated.FlowId = flowId;

        if (segmentResult.Outcome == ScenarioFlowSegmentOutcome.Failed)
        {
            updated.Status = ScenarioFlowRuntimeStatus.Failed;
            updated.FailureReason = segmentResult.ErrorMessage;
            await _store.SaveAsync(projectRoot, sessionId, scenarioId, updated, cancellationToken).ConfigureAwait(false);
            return FailResult(segmentResult.ErrorMessage ?? "Segment failed.", updated.ExecutionNodeId, updated.Status);
        }

        if (segmentResult.Outcome == ScenarioFlowSegmentOutcome.SuspendedWaitForInput)
        {
            // When photos arrive on the same turn as an attachment WaitForInput, continue immediately.
            if (attachmentIds.Count > 0
                && ScenarioFlowWaitForInputHelper.AcceptsAttachments(flowJson, updated.ExecutionNodeId))
            {
                var waitNodeId = updated.ExecutionNodeId;
                updated.Status = ScenarioFlowRuntimeStatus.Running;
                updated.PendingPrompt = null;
                // Advance past WaitForInput; otherwise the next segment re-enters the same node and recurses forever.
                updated.ExecutionNodeId = ScenarioFlowGraphNavigation.ResolveResumeTargetNode(flowJson, waitNodeId);
                ScenarioFlowLoopTraversal.ClearAttachmentDelta(updated);

                return await RunSegmentAndPersistAsync(
                    projectRoot,
                    sessionId,
                    scenarioId,
                    flowId,
                    flowJson,
                    updated,
                    userMessage,
                    Array.Empty<string>(),
                    cancellationToken).ConfigureAwait(false);
            }

            updated.Status = ScenarioFlowRuntimeStatus.WaitingForUserInput;
            ScenarioFlowLoopTraversal.ClearAttachmentDelta(updated);
            await _store.SaveAsync(projectRoot, sessionId, scenarioId, updated, cancellationToken).ConfigureAwait(false);
            return new ScenarioFlowRuntimeResult(
                true,
                false,
                updated.Status,
                updated.ExecutionNodeId,
                null,
                updated.PendingPrompt,
                null);
        }

        if (segmentResult.Outcome == ScenarioFlowSegmentOutcome.SuspendedAwaitEvent)
        {
            updated.Status = ScenarioFlowRuntimeStatus.WaitingForDomainEvent;
            var interim = ScenarioFlowInterimText.ForSnapshot(updated);
            await _store.SaveAsync(projectRoot, sessionId, scenarioId, updated, cancellationToken).ConfigureAwait(false);
            return new ScenarioFlowRuntimeResult(
                true,
                false,
                updated.Status,
                updated.ExecutionNodeId,
                interim,
                interim,
                null);
        }

        updated.Status = ScenarioFlowRuntimeStatus.Completed;
        updated.LastOutput = segmentResult.Output;
        updated.PendingPrompt = null;
        updated.AwaitingEvent = null;
        await _store.SaveAsync(projectRoot, sessionId, scenarioId, updated, cancellationToken).ConfigureAwait(false);

        return new ScenarioFlowRuntimeResult(
            true,
            true,
            ScenarioFlowRuntimeStatus.Completed,
            updated.ExecutionNodeId,
            segmentResult.Output,
            null,
            null);
    }

    private void ChangeState(ActorState newState, string reason)
    {
        if (_state == newState)
            return;
        var old = _state;
        _state = newState;
        StateChanged?.Invoke(this, new ActorStateChangedEventArgs(old, newState, reason));
    }

    private static ScenarioFlowRuntimeResult FailResult(string message, string executionNodeId, ScenarioFlowRuntimeStatus status) =>
        new(false, false, status, executionNodeId, null, null, message);
}
