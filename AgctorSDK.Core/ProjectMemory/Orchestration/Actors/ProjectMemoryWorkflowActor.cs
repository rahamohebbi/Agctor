using System;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;

namespace AgctorSDK.Core.ProjectMemory.Orchestration.Actors;

/// <summary>
/// Actor boundary for ProjectMemory workflow execution. The first PRD-020 slice
/// delegates to the existing pipeline runner so callers can adopt actor routing
/// before workflow internals are split into smaller actors.
/// </summary>
public sealed class ProjectMemoryWorkflowActor : IActor
{
    private readonly IProjectMemoryPipelineRunner _pipeline;
    private ActorState _state = ActorState.Initializing;

    public ProjectMemoryWorkflowActor(string id, IProjectMemoryPipelineRunner pipeline)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("Actor id is required.", nameof(id)) : id;
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public string Id { get; }
    public string ActorType => nameof(ProjectMemoryWorkflowActor);
    public ActorState State => _state;
    public event EventHandler<ActorStateChangedEventArgs>? StateChanged;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ChangeState(ActorState.Active, "Initialized");
        return Task.CompletedTask;
    }

    public async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (envelope.Payload is not ProjectMemoryWorkflowRequest request)
        {
            var failed = new ProjectMemoryWorkflowFailed(
                $"Unsupported ProjectMemory workflow payload '{envelope.Payload?.GetType().Name ?? "null"}'.");
            return AgctorEnvelopeBuilder.Error(envelope, Id, failed.Message);
        }

        try
        {
            var result = await _pipeline.RunAsync(request.PipelineRequest, cancellationToken).ConfigureAwait(false);
            return AgctorEnvelopeBuilder.Response(
                new ProjectMemoryWorkflowResult(result),
                envelope,
                Id,
                AgctorMessageTypes.Result);
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
        if (previous == newState)
            return;

        _state = newState;
        StateChanged?.Invoke(this, new ActorStateChangedEventArgs(previous, newState, reason));
    }
}

