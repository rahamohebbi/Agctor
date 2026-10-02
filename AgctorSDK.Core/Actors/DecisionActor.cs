using System;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Decisions;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;

namespace AgctorSDK.Core.Actors
{
    /// <summary>
    /// Runtime owner of decisions. Other actors reach it through <see cref="ActorContext"/>;
    /// the provider call happens on this actor's mailbox, not in the caller's process logic.
    /// </summary>
    public sealed class DecisionActor : IActor
    {
        private readonly IDecisionService _decisions;
        private ActorState _state = ActorState.Initializing;

        public DecisionActor(string id, IDecisionService decisions)
        {
            Id = string.IsNullOrWhiteSpace(id) ? DecisionFabricIds.DecisionActorId : id;
            _decisions = decisions ?? throw new ArgumentNullException(nameof(decisions));
        }

        public string Id { get; }
        public string ActorType => nameof(DecisionActor);
        public ActorState State => _state;
        public event EventHandler<ActorStateChangedEventArgs>? StateChanged;

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ChangeState(ActorState.Active, "Decision actor ready");
            return Task.CompletedTask;
        }

        public async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (request, context) = Unwrap(envelope.Payload);
            var result = await _decisions.DecideAsync(request, context, cancellationToken).ConfigureAwait(false);
            return new MessageEnvelope(result);
        }

        public Task ShutdownAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ChangeState(ActorState.Stopped, "Decision actor stopped");
            return Task.CompletedTask;
        }

        private static (DecisionRequest Request, DecisionContext Context) Unwrap(object? payload)
        {
            switch (payload)
            {
                case ActorDecisionMessage message when message.Request != null:
                    return (message.Request, new DecisionContext
                    {
                        ActorId = message.ActorId,
                        ActorType = message.ActorType,
                        Policy = message.Request.Policy
                    });
                case DecisionRequest request:
                    return (request, new DecisionContext { Policy = request.Policy });
                default:
                    throw new DecisionException(
                        $"Decision actor cannot handle payload '{payload?.GetType().Name ?? "null"}'.");
            }
        }

        private void ChangeState(ActorState newState, string reason)
        {
            var previous = _state;
            if (previous == newState)
            {
                return;
            }

            _state = newState;
            StateChanged?.Invoke(this, new ActorStateChangedEventArgs(previous, newState, reason));
        }
    }
}
