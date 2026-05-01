using System;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;

namespace AgctorSDK.Core.ProjectMemory.Orchestration.Actors;

/// <summary>
/// Actor boundary for approved generic inbox persistence. This keeps PRD-019
/// confirmation side effects message-routable without duplicating store logic.
/// </summary>
public sealed class ProjectMemoryGenericInboxActor : IActor
{
    private readonly IProjectMemoryPipelineRunner _pipeline;
    private ActorState _state = ActorState.Initializing;

    public ProjectMemoryGenericInboxActor(string id, IProjectMemoryPipelineRunner pipeline)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("Actor id is required.", nameof(id)) : id;
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public string Id { get; }
    public string ActorType => nameof(ProjectMemoryGenericInboxActor);
    public ActorState State => _state;
    public event EventHandler<ActorStateChangedEventArgs>? StateChanged;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ChangeState(ActorState.Active, "Initialized");
        return Task.CompletedTask;
    }

    public async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (envelope.Payload is not ProjectMemoryGenericInboxPersistRequest request)
        {
            return AgctorEnvelopeBuilder.Error(envelope, Id, $"Unsupported generic inbox payload '{envelope.Payload?.GetType().Name ?? "null"}'.");
        }

        try
        {
            var result = await _pipeline
                .PersistApprovedGenericFactsAsync(request.ProjectRoot, request.ScenarioId, request.Approvals, cancellationToken)
                .ConfigureAwait(false);
            return AgctorEnvelopeBuilder.Response(new ProjectMemoryGenericInboxPersistResult(result), envelope, Id, AgctorMessageTypes.Result);
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

    private void ChangeState(ActorState newState, string reason)
    {
        var previous = _state;
        if (previous == newState) return;
        _state = newState;
        StateChanged?.Invoke(this, new ActorStateChangedEventArgs(previous, newState, reason));
    }
}

