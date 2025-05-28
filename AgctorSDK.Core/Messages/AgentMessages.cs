using System;
using AgctorSDK.Core.Interfaces;

namespace AgctorSDK.Core.Messages
{
    /// <summary>
    /// Base class for all agent-related messages.
    /// Provides common properties for agent communication.
    /// </summary>
    public abstract class AgentMessage
    {
        /// <summary>
        /// Unique identifier for this message.
        /// </summary>
        public string MessageId { get; }

        /// <summary>
        /// Timestamp when the message was created.
        /// </summary>
        public DateTimeOffset Timestamp { get; }

        protected AgentMessage()
        {
            MessageId = Guid.NewGuid().ToString();
            Timestamp = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Message sent to an agent to process a new prompt or task.
    /// </summary>
    public class ProcessPromptMessage : AgentMessage
    {
        /// <summary>
        /// The prompt or task description for the agent to process.
        /// </summary>
        public string Prompt { get; }

        /// <summary>
        /// Optional correlation ID for tracking related messages.
        /// </summary>
        public string? CorrelationId { get; }

        public ProcessPromptMessage(string prompt, string? correlationId = null)
        {
            Prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
            CorrelationId = correlationId;
        }
    }

    /// <summary>
    /// Message sent to request the assignment of a subtask to a child agent.
    /// </summary>
    public class AssignSubtaskMessage : AgentMessage
    {
        /// <summary>
        /// The prompt or task description for the subtask.
        /// </summary>
        public string SubtaskPrompt { get; }

        /// <summary>
        /// Optional specific agent type to spawn for the subtask.
        /// </summary>
        public string? AgentType { get; }

        /// <summary>
        /// ID of the parent agent requesting the subtask assignment.
        /// </summary>
        public string ParentAgentId { get; }

        public AssignSubtaskMessage(string subtaskPrompt, string parentAgentId, string? agentType = null)
        {
            SubtaskPrompt = subtaskPrompt ?? throw new ArgumentNullException(nameof(subtaskPrompt));
            ParentAgentId = parentAgentId ?? throw new ArgumentNullException(nameof(parentAgentId));
            AgentType = agentType;
        }
    }

    /// <summary>
    /// Message sent by a child agent to report successful completion of a subtask.
    /// </summary>
    public class SubtaskCompletedMessage : AgentMessage
    {
        /// <summary>
        /// ID of the child agent that completed the subtask.
        /// </summary>
        public string ChildAgentId { get; }

        /// <summary>
        /// ID of the parent agent that assigned the subtask.
        /// </summary>
        public string ParentAgentId { get; }

        /// <summary>
        /// The result or output from the completed subtask.
        /// </summary>
        public object Result { get; }

        /// <summary>
        /// Optional correlation ID linking this completion to the original subtask assignment.
        /// </summary>
        public string? CorrelationId { get; }

        public SubtaskCompletedMessage(string childAgentId, string parentAgentId, object result, string? correlationId = null)
        {
            ChildAgentId = childAgentId ?? throw new ArgumentNullException(nameof(childAgentId));
            ParentAgentId = parentAgentId ?? throw new ArgumentNullException(nameof(parentAgentId));
            Result = result ?? throw new ArgumentNullException(nameof(result));
            CorrelationId = correlationId;
        }
    }

    /// <summary>
    /// Message sent by a child agent to report failure of a subtask.
    /// </summary>
    public class SubtaskFailedMessage : AgentMessage
    {
        /// <summary>
        /// ID of the child agent that failed the subtask.
        /// </summary>
        public string ChildAgentId { get; }

        /// <summary>
        /// ID of the parent agent that assigned the subtask.
        /// </summary>
        public string ParentAgentId { get; }

        /// <summary>
        /// The error or exception that caused the failure.
        /// </summary>
        public Exception Error { get; }

        /// <summary>
        /// Optional correlation ID linking this failure to the original subtask assignment.
        /// </summary>
        public string? CorrelationId { get; }

        public SubtaskFailedMessage(string childAgentId, string parentAgentId, Exception error, string? correlationId = null)
        {
            ChildAgentId = childAgentId ?? throw new ArgumentNullException(nameof(childAgentId));
            ParentAgentId = parentAgentId ?? throw new ArgumentNullException(nameof(parentAgentId));
            Error = error ?? throw new ArgumentNullException(nameof(error));
            CorrelationId = correlationId;
        }
    }

    /// <summary>
    /// Message sent to request the current status of an agent.
    /// </summary>
    public class GetAgentStatusMessage : AgentMessage
    {
        /// <summary>
        /// ID of the agent requesting status information.
        /// </summary>
        public string RequestingAgentId { get; }

        public GetAgentStatusMessage(string requestingAgentId)
        {
            RequestingAgentId = requestingAgentId ?? throw new ArgumentNullException(nameof(requestingAgentId));
        }
    }

    /// <summary>
    /// Response message containing agent status information.
    /// </summary>
    public class AgentStatusResponse : AgentMessage
    {
        /// <summary>
        /// ID of the agent whose status is being reported.
        /// </summary>
        public string AgentId { get; }

        /// <summary>
        /// Current status of the agent.
        /// </summary>
        public AgentStatus Status { get; }

        /// <summary>
        /// Current prompt the agent is working on (if any).
        /// </summary>
        public string? CurrentPrompt { get; }

        /// <summary>
        /// Number of active child agents.
        /// </summary>
        public int ActiveChildCount { get; }

        /// <summary>
        /// Additional status details or context.
        /// </summary>
        public string? Details { get; }

        public AgentStatusResponse(string agentId, AgentStatus status, string? currentPrompt = null, int activeChildCount = 0, string? details = null)
        {
            AgentId = agentId ?? throw new ArgumentNullException(nameof(agentId));
            Status = status;
            CurrentPrompt = currentPrompt;
            ActiveChildCount = activeChildCount;
            Details = details;
        }
    }

    /// <summary>
    /// Message sent to request an agent to stop its current work and shut down.
    /// </summary>
    public class StopAgentMessage : AgentMessage
    {
        /// <summary>
        /// Reason for stopping the agent.
        /// </summary>
        public string Reason { get; }

        /// <summary>
        /// Whether to force stop immediately or allow graceful shutdown.
        /// </summary>
        public bool ForceStop { get; }

        public StopAgentMessage(string reason, bool forceStop = false)
        {
            Reason = reason ?? throw new ArgumentNullException(nameof(reason));
            ForceStop = forceStop;
        }
    }
} 