using System;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;

namespace AgctorSDK.Core.ProjectMemory.Visual.Actors;

/// <summary>Gemma 4 vision memory extract → generic inbox (PRD-023d).</summary>
public sealed class VisualExtractActor : IActor
{
    private readonly VisualPipelineService _pipeline;
    private ActorState _state = ActorState.Initializing;

    public VisualExtractActor(string id, VisualPipelineService pipeline)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("Actor id is required.", nameof(id)) : id;
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public string Id { get; }
    public string ActorType => nameof(VisualExtractActor);
    public ActorState State => _state;
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
            if (envelope.Payload is VisualExtractRequest req)
            {
                var result = await _pipeline.ExtractAsync(req, cancellationToken).ConfigureAwait(false);
                return AgctorEnvelopeBuilder.Response(result, envelope, Id, AgctorMessageTypes.Result);
            }

            return AgctorEnvelopeBuilder.Error(
                envelope,
                Id,
                $"Unsupported visual extract payload '{envelope.Payload?.GetType().Name ?? "null"}'.");
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
        if (_state == newState)
            return;
        var old = _state;
        _state = newState;
        StateChanged?.Invoke(this, new ActorStateChangedEventArgs(old, newState, reason));
    }
}
