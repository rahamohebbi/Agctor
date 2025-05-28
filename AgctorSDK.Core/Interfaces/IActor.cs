using System;
using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.Interfaces
{
    /// <summary>
    /// Represents the core actor interface that all actors must implement.
    /// Provides the fundamental contract for message handling and actor lifecycle management.
    /// </summary>
    public interface IActor
    {
        /// <summary>
        /// Unique identifier for this actor instance.
        /// Used for message routing and actor reference management.
        /// </summary>
        string Id { get; }

        /// <summary>
        /// The type/class name of this actor.
        /// Used for actor spawning and type-based routing.
        /// </summary>
        string ActorType { get; }

        /// <summary>
        /// Current state of the actor (Active, Inactive, Stopping, etc.).
        /// Used for lifecycle management and health monitoring.
        /// </summary>
        ActorState State { get; }

        /// <summary>
        /// Processes an incoming message envelope.
        /// This is the primary method for actor message handling.
        /// </summary>
        /// <param name="envelope">The message envelope containing the message and metadata</param>
        /// <param name="cancellationToken">Token for cancelling the operation</param>
        /// <returns>A task representing the asynchronous message processing operation</returns>
        Task ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default);

        /// <summary>
        /// Initializes the actor when it's first created or activated.
        /// Called by the runtime before the actor starts processing messages.
        /// </summary>
        /// <param name="cancellationToken">Token for cancelling the operation</param>
        /// <returns>A task representing the asynchronous initialization operation</returns>
        Task InitializeAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gracefully shuts down the actor and cleans up resources.
        /// Called by the runtime when the actor is being deactivated or stopped.
        /// </summary>
        /// <param name="cancellationToken">Token for cancelling the operation</param>
        /// <returns>A task representing the asynchronous shutdown operation</returns>
        Task ShutdownAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Event raised when the actor's state changes.
        /// Useful for monitoring and debugging actor lifecycle.
        /// </summary>
        event EventHandler<ActorStateChangedEventArgs>? StateChanged;
    }

    /// <summary>
    /// Represents the possible states of an actor during its lifecycle.
    /// </summary>
    public enum ActorState
    {
        /// <summary>
        /// Actor is being created but not yet ready to receive messages.
        /// </summary>
        Initializing,

        /// <summary>
        /// Actor is active and processing messages.
        /// </summary>
        Active,

        /// <summary>
        /// Actor is temporarily inactive but can be reactivated.
        /// </summary>
        Inactive,

        /// <summary>
        /// Actor is in the process of shutting down.
        /// </summary>
        Stopping,

        /// <summary>
        /// Actor has been stopped and cannot process messages.
        /// </summary>
        Stopped,

        /// <summary>
        /// Actor encountered an error and is in a faulted state.
        /// </summary>
        Faulted
    }

    /// <summary>
    /// Event arguments for actor state change events.
    /// </summary>
    public class ActorStateChangedEventArgs : EventArgs
    {
        /// <summary>
        /// The previous state of the actor.
        /// </summary>
        public ActorState PreviousState { get; }

        /// <summary>
        /// The new state of the actor.
        /// </summary>
        public ActorState NewState { get; }

        /// <summary>
        /// Timestamp when the state change occurred.
        /// </summary>
        public DateTimeOffset Timestamp { get; }

        /// <summary>
        /// Optional reason for the state change.
        /// </summary>
        public string? Reason { get; }

        /// <summary>
        /// Initializes a new instance of the ActorStateChangedEventArgs class.
        /// </summary>
        /// <param name="previousState">The previous state</param>
        /// <param name="newState">The new state</param>
        /// <param name="reason">Optional reason for the change</param>
        public ActorStateChangedEventArgs(ActorState previousState, ActorState newState, string? reason = null)
        {
            PreviousState = previousState;
            NewState = newState;
            Timestamp = DateTimeOffset.UtcNow;
            Reason = reason;
        }
    }
} 