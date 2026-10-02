using System;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;

namespace AgctorSDK.Core.Decisions
{
    /// <summary>
    /// Asks the decision actor for a result using the runtime's request/response mailbox.
    /// </summary>
    public sealed class ActorContext : IActorContext
    {
        private readonly IActorRuntimeAdapter _runtime;
        private readonly TimeSpan _timeout;

        public ActorContext(string actorId, string? actorType, IActorRuntimeAdapter runtime, TimeSpan? timeout = null)
        {
            ActorId = actorId ?? throw new ArgumentNullException(nameof(actorId));
            ActorType = actorType;
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _timeout = timeout ?? TimeSpan.FromSeconds(30);
        }

        public string ActorId { get; }
        public string? ActorType { get; }

        public Task<DecisionResult> Decide(DecisionRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            // The decision actor correlates telemetry with the caller. Assign an id here so both sides share it.
            if (string.IsNullOrWhiteSpace(request.Id))
            {
                request.Id = Guid.NewGuid().ToString("N");
            }

            var message = new ActorDecisionMessage
            {
                ActorId = ActorId,
                ActorType = ActorType,
                Request = request
            };

            return _runtime.SendMessageAsync<DecisionResult>(
                DecisionFabricIds.DecisionActorId,
                message,
                _timeout,
                senderId: ActorId,
                cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// Small holder so actor bases can expose Context and Decide without copying the null check.
    /// </summary>
    public sealed class ActorContextSlot : IHasActorContext
    {
        public IActorContext? Context { get; private set; }

        public void BindContext(IActorContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public Task<DecisionResult> Decide(DecisionRequest request, CancellationToken cancellationToken = default)
        {
            if (Context == null)
            {
                throw new InvalidOperationException(
                    "Actor decision context is not bound. Spawn the actor on a runtime that hosts the decision fabric; call Context.Decide, not a provider.");
            }

            return Context.Decide(request, cancellationToken);
        }
    }
}
