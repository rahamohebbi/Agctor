using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.Interfaces
{
    /// <summary>
    /// Represents an adapter interface for different actor runtime backends.
    /// Enables hot-swappable actor model implementations (Orleans, Proto.Actor, wasmCloud, etc.).
    /// </summary>
    public interface IActorRuntimeAdapter : IDisposable
    {
        /// <summary>
        /// Name of the runtime adapter (e.g., "Orleans", "Proto.Actor", "InMemory").
        /// Used for identification and configuration purposes.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Version of the runtime adapter.
        /// Used for compatibility checking and debugging.
        /// </summary>
        string Version { get; }

        /// <summary>
        /// Indicates whether the runtime is currently initialized and ready to use.
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>
        /// Configuration properties specific to this runtime adapter.
        /// Allows for runtime-specific settings and customization.
        /// </summary>
        IReadOnlyDictionary<string, object> Configuration { get; }

        /// <summary>
        /// Initializes the actor runtime with the provided configuration.
        /// Must be called before any actor operations can be performed.
        /// </summary>
        /// <param name="configuration">Runtime-specific configuration parameters</param>
        /// <param name="cancellationToken">Token for cancelling the operation</param>
        /// <returns>A task representing the asynchronous initialization operation</returns>
        Task InitializeAsync(IDictionary<string, object> configuration, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gracefully shuts down the actor runtime and cleans up resources.
        /// Should stop all actors and release any held resources.
        /// </summary>
        /// <param name="cancellationToken">Token for cancelling the operation</param>
        /// <returns>A task representing the asynchronous shutdown operation</returns>
        Task ShutdownAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Spawns a new actor instance of the specified type with the given ID.
        /// The actor will be created and initialized before being returned.
        /// </summary>
        /// <typeparam name="T">The type of actor to spawn (must implement IActor)</typeparam>
        /// <param name="actorId">Unique identifier for the new actor instance</param>
        /// <param name="initializationData">Optional data to pass to the actor during initialization</param>
        /// <param name="cancellationToken">Token for cancelling the operation</param>
        /// <returns>A task containing the spawned actor instance</returns>
        Task<T> SpawnActorAsync<T>(string actorId, object? initializationData = null, CancellationToken cancellationToken = default) where T : class, IActor;

        /// <summary>
        /// Spawns a new actor instance using a factory function.
        /// The actor will be created and initialized before being returned.
        /// </summary>
        /// <typeparam name="T">The type of actor to spawn (must implement IActor)</typeparam>
        /// <param name="actorId">Unique identifier for the new actor instance</param>
        /// <param name="actorFactory">A function that creates an instance of the actor.</param>
        /// <param name="initializationData">Optional data to pass to the actor during initialization</param>
        /// <param name="cancellationToken">Token for cancelling the operation</param>
        /// <returns>A task containing the spawned actor instance</returns>
        Task<T> SpawnActorAsync<T>(string actorId, Func<string, T> actorFactory, object? initializationData = null, CancellationToken cancellationToken = default) where T : class, IActor;

        /// <summary>
        /// Gets a reference to an existing actor by its ID.
        /// Returns null if the actor doesn't exist or is not accessible.
        /// </summary>
        /// <typeparam name="T">The type of actor to retrieve</typeparam>
        /// <param name="actorId">The unique identifier of the actor</param>
        /// <param name="cancellationToken">Token for cancelling the operation</param>
        /// <returns>A task containing the actor reference or null if not found</returns>
        Task<T?> GetActorAsync<T>(string actorId, CancellationToken cancellationToken = default) where T : class, IActor;

        /// <summary>
        /// Sends a message to the specified actor.
        /// The message will be wrapped in an envelope and routed to the target actor.
        /// </summary>
        /// <param name="targetActorId">The ID of the actor to send the message to</param>
        /// <param name="message">The message payload to send</param>
        /// <param name="senderId">Optional ID of the sending actor</param>
        /// <param name="headers">Optional custom headers for the message</param>
        /// <param name="cancellationToken">Token for cancelling the operation</param>
        /// <returns>A task representing the asynchronous send operation</returns>
        Task SendMessageAsync(string targetActorId, object message, string? senderId = null, 
            IDictionary<string, string>? headers = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a message and waits for a response from the target actor.
        /// Implements request-response pattern with timeout support.
        /// </summary>
        /// <typeparam name="TResponse">The expected type of the response</typeparam>
        /// <param name="targetActorId">The ID of the actor to send the message to</param>
        /// <param name="message">The message payload to send</param>
        /// <param name="timeout">Maximum time to wait for a response</param>
        /// <param name="senderId">Optional ID of the sending actor</param>
        /// <param name="headers">Optional custom headers for the message</param>
        /// <param name="cancellationToken">Token for cancelling the operation</param>
        /// <returns>A task containing the response from the target actor</returns>
        Task<TResponse> SendMessageAsync<TResponse>(string targetActorId, object message, TimeSpan timeout,
            string? senderId = null, IDictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
            where TResponse : class;

        /// <summary>
        /// Stops and removes an actor from the runtime.
        /// The actor will be gracefully shut down and its resources cleaned up.
        /// </summary>
        /// <param name="actorId">The ID of the actor to stop</param>
        /// <param name="cancellationToken">Token for cancelling the operation</param>
        /// <returns>A task representing the asynchronous stop operation</returns>
        Task StopActorAsync(string actorId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets a list of all active actor IDs in the runtime.
        /// Useful for monitoring and debugging purposes.
        /// </summary>
        /// <param name="cancellationToken">Token for cancelling the operation</param>
        /// <returns>A task containing the list of active actor IDs</returns>
        Task<IEnumerable<string>> GetActiveActorIdsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets runtime statistics and health information.
        /// Provides insights into the current state of the actor system.
        /// </summary>
        /// <param name="cancellationToken">Token for cancelling the operation</param>
        /// <returns>A task containing runtime statistics</returns>
        Task<IRuntimeStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Event raised when an actor is spawned in the runtime.
        /// </summary>
        event EventHandler<ActorSpawnedEventArgs>? ActorSpawned;

        /// <summary>
        /// Event raised when an actor is stopped in the runtime.
        /// </summary>
        event EventHandler<ActorStoppedEventArgs>? ActorStopped;

        /// <summary>
        /// Event raised when a message is sent through the runtime.
        /// Useful for monitoring and debugging message flow.
        /// </summary>
        event EventHandler<MessageSentEventArgs>? MessageSent;

        /// <summary>
        /// New method for requesting human input, added for Human Agent Fallback (prd-cli-001.md).
        /// The runtime adapter implementation is responsible for interacting with the user (e.g., via CLI).
        /// </summary>
        /// <param name="requestingAgentId">The ID of the agent requesting human input.</param>
        /// <param name="prompt">The prompt or question to display to the human.</param>
        /// <param name="instructions">Instructions for the human on how to submit their input (e.g., end token).</param>
        /// <param name="cancellationToken">Token for cancelling the operation.</param>
        /// <returns>A task containing the string input provided by the human.</returns>
        Task<string> RequestHumanInputAsync(string requestingAgentId, string prompt, string instructions, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Represents runtime statistics and health information.
    /// </summary>
    public interface IRuntimeStatistics
    {
        /// <summary>
        /// Total number of active actors in the runtime.
        /// </summary>
        int ActiveActorCount { get; }

        /// <summary>
        /// Total number of messages processed since runtime startup.
        /// </summary>
        long TotalMessagesProcessed { get; }

        /// <summary>
        /// Current messages per second throughput.
        /// </summary>
        double MessagesPerSecond { get; }

        /// <summary>
        /// Average message processing time in milliseconds.
        /// </summary>
        double AverageMessageProcessingTime { get; }

        /// <summary>
        /// Runtime uptime since initialization.
        /// </summary>
        TimeSpan Uptime { get; }

        /// <summary>
        /// Memory usage statistics for the runtime.
        /// </summary>
        long MemoryUsageBytes { get; }

        /// <summary>
        /// Additional runtime-specific metrics.
        /// </summary>
        IReadOnlyDictionary<string, object> AdditionalMetrics { get; }
    }

    /// <summary>
    /// Event arguments for actor spawned events.
    /// </summary>
    public class ActorSpawnedEventArgs : EventArgs
    {
        public string ActorId { get; }
        public string ActorType { get; }
        public DateTimeOffset Timestamp { get; }

        public ActorSpawnedEventArgs(string actorId, string actorType)
        {
            ActorId = actorId;
            ActorType = actorType;
            Timestamp = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Event arguments for actor stopped events.
    /// </summary>
    public class ActorStoppedEventArgs : EventArgs
    {
        public string ActorId { get; }
        public string ActorType { get; }
        public DateTimeOffset Timestamp { get; }
        public string? Reason { get; }

        public ActorStoppedEventArgs(string actorId, string actorType, string? reason = null)
        {
            ActorId = actorId;
            ActorType = actorType;
            Timestamp = DateTimeOffset.UtcNow;
            Reason = reason;
        }
    }

    /// <summary>
    /// Event arguments for message sent events.
    /// </summary>
    public class MessageSentEventArgs : EventArgs
    {
        public string MessageId { get; }
        public string? SenderId { get; }
        public string ReceiverId { get; }
        public string MessageType { get; }
        public DateTimeOffset Timestamp { get; }

        public MessageSentEventArgs(string messageId, string? senderId, string receiverId, string messageType)
        {
            MessageId = messageId;
            SenderId = senderId;
            ReceiverId = receiverId;
            MessageType = messageType;
            Timestamp = DateTimeOffset.UtcNow;
        }
    }
} 