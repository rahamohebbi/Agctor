using System;
using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.Interfaces
{
    /// <summary>
    /// Interface for timeout supervision functionality.
    /// Manages timeout monitoring and handling for agent operations using message-based communication.
    /// </summary>
    public interface ITimeoutSupervisor : IActor
    {
        /// <summary>
        /// Registers an agent operation for timeout monitoring.
        /// Creates a scheduled timeout message that will trigger if the operation doesn't complete in time.
        /// </summary>
        /// <param name="agentId">ID of the agent to monitor</param>
        /// <param name="operationId">Unique identifier for the operation being monitored</param>
        /// <param name="context">Context information for timeout policy decisions</param>
        /// <param name="cancellationToken">Token for cancelling the registration</param>
        /// <returns>Task representing the registration operation</returns>
        Task RegisterTimeoutAsync(string agentId, string operationId, AgentContext context, CancellationToken cancellationToken = default);

        /// <summary>
        /// Cancels timeout monitoring for a completed or cancelled operation.
        /// Removes the scheduled timeout message to prevent false timeout triggers.
        /// </summary>
        /// <param name="agentId">ID of the agent</param>
        /// <param name="operationId">ID of the operation to stop monitoring</param>
        /// <param name="cancellationToken">Token for cancelling the cancellation</param>
        /// <returns>Task representing the cancellation operation</returns>
        Task CancelTimeoutAsync(string agentId, string operationId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Updates progress information for an ongoing operation.
        /// Used by timeout policies to determine if timeouts should be extended or operations should continue.
        /// </summary>
        /// <param name="agentId">ID of the agent</param>
        /// <param name="operationId">ID of the operation</param>
        /// <param name="progress">Current progress information</param>
        /// <param name="cancellationToken">Token for cancelling the update</param>
        /// <returns>Task representing the progress update operation</returns>
        Task UpdateProgressAsync(string agentId, string operationId, AgentProgress progress, CancellationToken cancellationToken = default);

        /// <summary>
        /// Forces an immediate timeout check for a specific operation.
        /// Useful for testing or manual timeout triggers.
        /// </summary>
        /// <param name="agentId">ID of the agent</param>
        /// <param name="operationId">ID of the operation</param>
        /// <param name="cancellationToken">Token for cancelling the check</param>
        /// <returns>Task representing the timeout check operation</returns>
        Task CheckTimeoutAsync(string agentId, string operationId, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Configuration options for timeout supervision behavior.
    /// </summary>
    public class TimeoutSupervisorOptions
    {
        /// <summary>
        /// Default timeout duration when no policy is specified or policy returns null.
        /// </summary>
        public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Maximum number of timeout reschedules allowed per operation.
        /// Prevents infinite timeout extensions.
        /// </summary>
        public int MaxRescheduleCount { get; set; } = 3;

        /// <summary>
        /// Whether to log timeout events for auditing and debugging.
        /// </summary>
        public bool EnableTimeoutLogging { get; set; } = true;

        /// <summary>
        /// Whether to attempt collecting partial results on timeout.
        /// </summary>
        public bool CollectPartialResultsOnTimeout { get; set; } = true;

        /// <summary>
        /// Grace period to wait for partial results collection.
        /// </summary>
        public TimeSpan PartialResultsGracePeriod { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Whether to notify parent agents when child agents timeout.
        /// </summary>
        public bool NotifyParentOnChildTimeout { get; set; } = true;
    }

    /// <summary>
    /// Represents the result of a timeout operation.
    /// Contains information about what action was taken and any partial results collected.
    /// </summary>
    public class TimeoutResult
    {
        /// <summary>
        /// The action taken when the timeout occurred.
        /// </summary>
        public TimeoutAction Action { get; }

        /// <summary>
        /// Partial results that were collected before timeout (if any).
        /// </summary>
        public object? PartialResults { get; }

        /// <summary>
        /// Additional details about the timeout handling.
        /// </summary>
        public string? Details { get; }

        /// <summary>
        /// When the timeout occurred.
        /// </summary>
        public DateTimeOffset TimeoutTimestamp { get; }

        /// <summary>
        /// How long the operation ran before timing out.
        /// </summary>
        public TimeSpan ActualDuration { get; }

        public TimeoutResult(TimeoutAction action, object? partialResults = null, string? details = null, TimeSpan? actualDuration = null)
        {
            Action = action;
            PartialResults = partialResults;
            Details = details;
            TimeoutTimestamp = DateTimeOffset.UtcNow;
            ActualDuration = actualDuration ?? TimeSpan.Zero;
        }
    }

    /// <summary>
    /// Represents the different actions that can be taken when a timeout occurs.
    /// </summary>
    public enum TimeoutAction
    {
        /// <summary>
        /// Cancel the operation and return any partial results.
        /// </summary>
        Cancel,

        /// <summary>
        /// Escalate to parent agent or supervisor for decision.
        /// </summary>
        Escalate,

        /// <summary>
        /// Retry the operation with potentially different parameters.
        /// </summary>
        Retry,

        /// <summary>
        /// Abort the operation immediately without collecting results.
        /// </summary>
        Abort,

        /// <summary>
        /// Extend the timeout and continue monitoring.
        /// </summary>
        Extend
    }
} 