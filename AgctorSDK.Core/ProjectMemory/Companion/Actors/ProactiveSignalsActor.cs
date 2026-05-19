using System;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.ProjectMemory.LifeSignals;

namespace AgctorSDK.Core.ProjectMemory.Companion.Actors;

/// <summary>
/// Actor boundary for read-only birthday/contact/timeline nudges (playground Reminders).
/// </summary>
public sealed class ProactiveSignalsActor : IActor
{
    private ActorState _state = ActorState.Initializing;

    public ProactiveSignalsActor(string id)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("Actor id is required.", nameof(id)) : id;
    }

    public string Id { get; }
    public string ActorType => nameof(ProactiveSignalsActor);
    public ActorState State => _state;
    public event EventHandler<ActorStateChangedEventArgs>? StateChanged;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ChangeState(ActorState.Active, "Initialized");
        return Task.CompletedTask;
    }

    public Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (envelope.Payload is not ProactiveSignalsWorkflowRequest request)
        {
            return Task.FromResult<IMessageEnvelope>(AgctorEnvelopeBuilder.Error(
                envelope,
                Id,
                $"Unsupported proactive signals payload '{envelope.Payload?.GetType().Name ?? "null"}'."));
        }

        var signals = PersonLifeSignalsReader.Scan(
            request.ProjectRoot,
            request.ScenarioId,
            staleContactDays: request.StaleContactDays,
            birthdayHorizonDays: request.BirthdayHorizonDays);

        var result = new ProactiveSignalsWorkflowResult(signals);
        return Task.FromResult<IMessageEnvelope>(
            AgctorEnvelopeBuilder.Response(result, envelope, Id, AgctorMessageTypes.Result));
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
