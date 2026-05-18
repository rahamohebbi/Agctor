using System;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;

namespace AgctorSDK.Core.Agents;

/// <summary>Minimal <see cref="IActor"/> primitive shared by tool actors and lightweight orchestrators.</summary>
public abstract class BaseActor : IActor
{
    public string Id { get; }
    public string ActorType { get; }
    public ActorState State { get; protected set; }

    public event EventHandler<ActorStateChangedEventArgs>? StateChanged;

    protected BaseActor(string id, string actorType)
    {
        Id = id;
        ActorType = actorType;
        State = ActorState.Initializing;
    }

    public virtual Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ChangeState(ActorState.Active, "Initialized successfully");
        return Task.CompletedTask;
    }

    public abstract Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default);

    public virtual Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        ChangeState(ActorState.Stopped, "Shutdown completed");
        return Task.CompletedTask;
    }

    protected void ChangeState(ActorState newState, string? reason = null)
    {
        var previousState = State;
        if (previousState == newState)
            return;

        State = newState;
        StateChanged?.Invoke(this, new ActorStateChangedEventArgs(previousState, newState, reason));
    }
}
