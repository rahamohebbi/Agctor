using System;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;

namespace AgctorSDK.Core.ProjectMemory.Visual.Actors;

/// <summary>Actor mailbox for visual privacy purge (forget-person companion).</summary>
public sealed class VisualPrivacyActor : IActor
{
    private readonly IVisualPersonPrivacyPurge _purge;
    private ActorState _state = ActorState.Initializing;

    public VisualPrivacyActor(string id, IVisualPersonPrivacyPurge purge)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("Actor id is required.", nameof(id)) : id;
        _purge = purge ?? throw new ArgumentNullException(nameof(purge));
    }

    public string Id { get; }
    public string ActorType => nameof(VisualPrivacyActor);
    public ActorState State => _state;
    public event EventHandler<ActorStateChangedEventArgs>? StateChanged;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ChangeState(ActorState.Active, "Initialized");
        return Task.CompletedTask;
    }

    public async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (envelope.Payload is not VisualPrivacyPurgeRequest request)
        {
            return AgctorEnvelopeBuilder.Error(
                envelope,
                Id,
                $"Unsupported visual privacy payload '{envelope.Payload?.GetType().Name ?? "null"}'.");
        }

        try
        {
            var result = await _purge
                .PurgePersonAsync(request.ProjectRoot, request.ScenarioId, request.EntityKey, cancellationToken)
                .ConfigureAwait(false);
            return AgctorEnvelopeBuilder.Response(
                new VisualPrivacyPurgeWorkflowResult(
                    Success: true,
                    AssetsRemoved: result.AssetsRemoved,
                    BlobsDeleted: result.BlobsDeleted,
                    Error: null),
                envelope,
                Id,
                AgctorMessageTypes.Result);
        }
        catch (Exception ex)
        {
            return AgctorEnvelopeBuilder.Response(
                new VisualPrivacyPurgeWorkflowResult(false, 0, 0, ex.Message),
                envelope,
                Id,
                AgctorMessageTypes.Result);
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
