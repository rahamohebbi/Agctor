using System;
using System.Collections.Generic;

namespace AgctorSDK.Core.Interfaces
{
    /// <summary>
    /// Defines timeout policies for agent operations.
    /// Provides configurable timeout behavior based on agent context and progress.
    /// This interface supports pluggable timeout strategies for different agent types and scenarios.
    /// </summary>
    public interface ITimeoutPolicy
    {
        /// <summary>
        /// Determines the timeout duration for an agent operation based on its context.
        /// This allows for dynamic timeout calculation based on agent type, task complexity, etc.
        /// </summary>
        /// <param name="context">The agent context containing task and environment information</param>
        /// <returns>The timeout duration for the operation</returns>
        TimeSpan GetTimeout(AgentContext context);

        /// <summary>
        /// Determines whether an operation should be rescheduled based on current progress.
        /// Useful for extending timeouts when an agent is making meaningful progress.
        /// </summary>
        /// <param name="context">The agent context</param>
        /// <param name="progress">Current progress information from the agent</param>
        /// <returns>True if the timeout should be rescheduled (extended), false otherwise</returns>
        bool ShouldReschedule(AgentContext context, AgentProgress progress);

        /// <summary>
        /// Determines whether an operation should be aborted immediately based on agent state.
        /// Allows for early termination in cases where continuing would be futile.
        /// </summary>
        /// <param name="context">The agent context</param>
        /// <param name="state">Current state of the agent</param>
        /// <returns>True if the operation should be aborted, false to continue</returns>
        bool ShouldAbort(AgentContext context, ActorState state);
    }

    /// <summary>
    /// Provides context information about an agent and its current operation.
    /// Used by timeout policies to make informed decisions about timeout behavior.
    /// </summary>
    public class AgentContext
    {
        /// <summary>
        /// Unique identifier of the agent.
        /// </summary>
        public string AgentId { get; }

        /// <summary>
        /// Type of the agent (e.g., "LLMAgent", "CodeExecutorAgent").
        /// </summary>
        public string AgentType { get; }

        /// <summary>
        /// Current prompt or task the agent is working on.
        /// </summary>
        public string? CurrentPrompt { get; }

        /// <summary>
        /// ID of the parent agent if this is a child agent.
        /// </summary>
        public string? ParentAgentId { get; }

        /// <summary>
        /// Number of child agents spawned by this agent.
        /// </summary>
        public int ChildAgentCount { get; }

        /// <summary>
        /// Estimated complexity or priority of the current task.
        /// Higher values indicate more complex tasks that may need longer timeouts.
        /// </summary>
        public int TaskComplexity { get; }

        /// <summary>
        /// Maximum timeout budget allocated from parent agent.
        /// Used for timeout propagation and budget management.
        /// </summary>
        public TimeSpan? TimeoutBudget { get; }

        /// <summary>
        /// When the current operation started.
        /// </summary>
        public DateTimeOffset OperationStartTime { get; }

        /// <summary>
        /// Additional metadata for policy decision making.
        /// </summary>
        public Dictionary<string, object> Metadata { get; }

        public AgentContext(
            string agentId,
            string agentType,
            string? currentPrompt = null,
            string? parentAgentId = null,
            int childAgentCount = 0,
            int taskComplexity = 1,
            TimeSpan? timeoutBudget = null,
            Dictionary<string, object>? metadata = null)
        {
            AgentId = agentId ?? throw new ArgumentNullException(nameof(agentId));
            AgentType = agentType ?? throw new ArgumentNullException(nameof(agentType));
            CurrentPrompt = currentPrompt;
            ParentAgentId = parentAgentId;
            ChildAgentCount = childAgentCount;
            TaskComplexity = Math.Max(1, taskComplexity);
            TimeoutBudget = timeoutBudget;
            OperationStartTime = DateTimeOffset.UtcNow;
            Metadata = metadata ?? new Dictionary<string, object>();
        }
    }

    /// <summary>
    /// Represents progress information from an agent operation.
    /// Used by timeout policies to determine if meaningful progress is being made.
    /// </summary>
    public class AgentProgress
    {
        /// <summary>
        /// Percentage completion (0.0 to 1.0).
        /// </summary>
        public double CompletionPercentage { get; }

        /// <summary>
        /// Number of subtasks completed.
        /// </summary>
        public int CompletedSubtasks { get; }

        /// <summary>
        /// Total number of subtasks identified.
        /// </summary>
        public int TotalSubtasks { get; }

        /// <summary>
        /// When the last progress update occurred.
        /// </summary>
        public DateTimeOffset LastProgressUpdate { get; }

        /// <summary>
        /// Optional description of current activity.
        /// </summary>
        public string? CurrentActivity { get; }

        /// <summary>
        /// Whether the agent is currently making active progress.
        /// </summary>
        public bool IsActivelyProgressing { get; }

        /// <summary>
        /// Partial results available so far (if any).
        /// </summary>
        public object? PartialResults { get; }

        public AgentProgress(
            double completionPercentage,
            int completedSubtasks = 0,
            int totalSubtasks = 0,
            string? currentActivity = null,
            bool isActivelyProgressing = true,
            object? partialResults = null)
        {
            CompletionPercentage = Math.Max(0.0, Math.Min(1.0, completionPercentage));
            CompletedSubtasks = Math.Max(0, completedSubtasks);
            TotalSubtasks = Math.Max(0, totalSubtasks);
            CurrentActivity = currentActivity;
            IsActivelyProgressing = isActivelyProgressing;
            PartialResults = partialResults;
            LastProgressUpdate = DateTimeOffset.UtcNow;
        }
    }
} 