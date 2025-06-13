using System;
using AgctorSDK.Core.Interfaces;

namespace AgctorSDK.Core.Messages
{
    /// <summary>
    /// Base class for all timeout-related messages.
    /// Extends the common AgentMessage with timeout-specific properties.
    /// </summary>
    public abstract class TimeoutMessage : AgentMessage
    {
        /// <summary>
        /// ID of the agent being monitored for timeout.
        /// </summary>
        public string AgentId { get; }

        /// <summary>
        /// Unique identifier for the specific operation being monitored.
        /// </summary>
        public string OperationId { get; }

        protected TimeoutMessage(string agentId, string operationId)
        {
            AgentId = agentId ?? throw new ArgumentNullException(nameof(agentId));
            OperationId = operationId ?? throw new ArgumentNullException(nameof(operationId));
        }
    }

    /// <summary>
    /// Message sent to register an operation for timeout monitoring.
    /// Creates a scheduled timeout that will trigger if the operation doesn't complete.
    /// </summary>
    public class RegisterTimeoutMessage : TimeoutMessage
    {
        /// <summary>
        /// Context information for the operation being monitored.
        /// </summary>
        public AgentContext Context { get; }

        /// <summary>
        /// Optional timeout policy to use for this specific operation.
        /// If null, the supervisor's default policy will be used.
        /// </summary>
        public ITimeoutPolicy? TimeoutPolicy { get; }

        public RegisterTimeoutMessage(string agentId, string operationId, AgentContext context, ITimeoutPolicy? timeoutPolicy = null)
            : base(agentId, operationId)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            TimeoutPolicy = timeoutPolicy;
        }
    }

    /// <summary>
    /// Message sent to cancel timeout monitoring for an operation.
    /// Prevents timeout triggers for operations that have completed or been cancelled.
    /// </summary>
    public class CancelTimeoutMessage : TimeoutMessage
    {
        /// <summary>
        /// Reason for cancelling the timeout monitoring.
        /// </summary>
        public string Reason { get; }

        /// <summary>
        /// Optional results from the completed operation.
        /// </summary>
        public object? Results { get; }

        public CancelTimeoutMessage(string agentId, string operationId, string reason, object? results = null)
            : base(agentId, operationId)
        {
            Reason = reason ?? throw new ArgumentNullException(nameof(reason));
            Results = results;
        }
    }

    /// <summary>
    /// Message sent to update progress information for an ongoing operation.
    /// Used by timeout policies to make decisions about extending or aborting operations.
    /// </summary>
    public class UpdateProgressMessage : TimeoutMessage
    {
        /// <summary>
        /// Current progress information for the operation.
        /// </summary>
        public AgentProgress Progress { get; }

        public UpdateProgressMessage(string agentId, string operationId, AgentProgress progress)
            : base(agentId, operationId)
        {
            Progress = progress ?? throw new ArgumentNullException(nameof(progress));
        }
    }

    /// <summary>
    /// Internal message used by the timeout supervisor for scheduled timeout checks.
    /// This message is sent to itself using delayed message scheduling.
    /// </summary>
    public class TimeoutTriggerMessage : TimeoutMessage
    {
        /// <summary>
        /// When this timeout was originally scheduled.
        /// </summary>
        public DateTimeOffset ScheduledTime { get; }

        /// <summary>
        /// Number of times this timeout has been rescheduled.
        /// </summary>
        public int RescheduleCount { get; }

        /// <summary>
        /// Original timeout duration that was scheduled.
        /// </summary>
        public TimeSpan OriginalTimeout { get; }

        public TimeoutTriggerMessage(string agentId, string operationId, DateTimeOffset scheduledTime, TimeSpan originalTimeout, int rescheduleCount = 0)
            : base(agentId, operationId)
        {
            ScheduledTime = scheduledTime;
            OriginalTimeout = originalTimeout;
            RescheduleCount = Math.Max(0, rescheduleCount);
        }
    }

    /// <summary>
    /// Message sent to request an immediate timeout check for a specific operation.
    /// Useful for testing or manual timeout triggers.
    /// </summary>
    public class CheckTimeoutMessage : TimeoutMessage
    {
        /// <summary>
        /// Whether to force the timeout even if the operation is still progressing.
        /// </summary>
        public bool ForceTimeout { get; }

        public CheckTimeoutMessage(string agentId, string operationId, bool forceTimeout = false)
            : base(agentId, operationId)
        {
            ForceTimeout = forceTimeout;
        }
    }

    /// <summary>
    /// Message sent when a timeout occurs to notify relevant parties.
    /// Contains information about the timeout and any actions taken.
    /// </summary>
    public class TimeoutOccurredMessage : TimeoutMessage
    {
        /// <summary>
        /// The result of the timeout handling, including action taken and partial results.
        /// </summary>
        public TimeoutResult Result { get; }

        /// <summary>
        /// Context information for the operation that timed out.
        /// </summary>
        public AgentContext Context { get; }

        /// <summary>
        /// ID of the parent agent to notify (if any).
        /// </summary>
        public string? ParentAgentId { get; }

        public TimeoutOccurredMessage(string agentId, string operationId, TimeoutResult result, AgentContext context, string? parentAgentId = null)
            : base(agentId, operationId)
        {
            Result = result ?? throw new ArgumentNullException(nameof(result));
            Context = context ?? throw new ArgumentNullException(nameof(context));
            ParentAgentId = parentAgentId;
        }
    }

    /// <summary>
    /// Message sent to request partial results from an agent before timeout.
    /// Allows collecting any work completed so far.
    /// </summary>
    public class CollectPartialResultsMessage : TimeoutMessage
    {
        /// <summary>
        /// Maximum time to wait for partial results collection.
        /// </summary>
        public TimeSpan GracePeriod { get; }

        public CollectPartialResultsMessage(string agentId, string operationId, TimeSpan gracePeriod)
            : base(agentId, operationId)
        {
            GracePeriod = gracePeriod;
        }
    }

    /// <summary>
    /// Response message containing partial results from an agent.
    /// </summary>
    public class PartialResultsResponse : TimeoutMessage
    {
        /// <summary>
        /// Partial results collected from the agent.
        /// </summary>
        public object? PartialResults { get; }

        /// <summary>
        /// Current progress information.
        /// </summary>
        public AgentProgress? Progress { get; }

        /// <summary>
        /// Whether the agent can continue working if given more time.
        /// </summary>
        public bool CanContinue { get; }

        public PartialResultsResponse(string agentId, string operationId, object? partialResults = null, AgentProgress? progress = null, bool canContinue = false)
            : base(agentId, operationId)
        {
            PartialResults = partialResults;
            Progress = progress;
            CanContinue = canContinue;
        }
    }
} 