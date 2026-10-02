using System;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Decisions;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;

namespace AgctorSDK.Core.Agents
{
    public abstract class BaseActor : IActor, IHasActorContext
    {
        private readonly ActorContextSlot _decisions = new ActorContextSlot();

        public string Id { get; }
        public string ActorType { get; }
        public ActorState State { get; protected set; }

        /// <summary>
        /// Runtime-bound gateway. Decide goes to the decision actor, not to a provider.
        /// </summary>
        public IActorContext? Context => _decisions.Context;

        public event EventHandler<ActorStateChangedEventArgs>? StateChanged;

        protected BaseActor(string id)
            : this(id, string.Empty)
        {
        }

        protected BaseActor(string id, string actorType)
        {
            Id = id;
            // GetType() is the concrete actor, so a one-arg constructor still reports the right type.
            ActorType = string.IsNullOrWhiteSpace(actorType) ? GetType().Name : actorType;
            State = ActorState.Initializing;
        }

        public void BindContext(IActorContext context)
        {
            _decisions.BindContext(context);
        }

        /// <summary>
        /// Asks the decision fabric. Available only after the runtime binds <see cref="Context"/>.
        /// </summary>
        protected Task<DecisionResult> Decide(DecisionRequest request, CancellationToken cancellationToken = default)
        {
            return _decisions.Decide(request, cancellationToken);
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
            {
                return;
            }

            State = newState;
            StateChanged?.Invoke(this, new ActorStateChangedEventArgs(previousState, newState, reason));
        }
    }
} 