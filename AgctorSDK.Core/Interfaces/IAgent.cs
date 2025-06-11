using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.Interfaces
{
    /// <summary>
    /// Represents an intelligent agent that can process prompts and spawn child agents for subtasks.
    /// Extends the basic IActor interface with agent-specific capabilities for recursive task decomposition.
    /// </summary>
    public interface IAgent : IActor
    {
        /// <summary>
        /// The current prompt or task that this agent is working on.
        /// This represents the agent's primary objective or instruction.
        /// </summary>
        string? CurrentPrompt { get; }

        /// <summary>
        /// The parent agent ID if this agent was spawned as a child agent.
        /// Null for root-level agents that were not spawned by other agents.
        /// </summary>
        string? ParentAgentId { get; }

        /// <summary>
        /// Collection of child agent IDs that this agent has spawned for subtasks.
        /// Used for tracking and managing the agent hierarchy.
        /// </summary>
        IReadOnlyList<string> ChildAgentIds { get; }

        /// <summary>
        /// The current status of the agent's work on its assigned prompt.
        /// Indicates whether the agent is idle, working, completed, or failed.
        /// </summary>
        AgentStatus Status { get; }
        
        /// <summary>
        /// The display name of the agent.
        /// This is used for visualization purposes.
        /// </summary>
        string? Name { get; }
        
        /// <summary>
        /// A description of the agent's purpose or function.
        /// This is used for visualization purposes.
        /// </summary>
        string? Description { get; }

        /// <summary>
        /// Processes a new prompt and begins working on the assigned task.
        /// The agent may decompose the task into subtasks and spawn child agents as needed.
        /// </summary>
        /// <param name="prompt">The prompt or task description to process</param>
        /// <param name="cancellationToken">Token for cancelling the operation</param>
        /// <returns>A task representing the asynchronous prompt processing operation</returns>
        Task ProcessPromptAsync(string prompt, CancellationToken cancellationToken = default);

        /// <summary>
        /// Assigns a subtask to a child agent by spawning a new agent instance.
        /// Uses the injected IAgentFactory to create and initialize the child agent.
        /// </summary>
        /// <param name="subtaskPrompt">The prompt or task description for the subtask</param>
        /// <param name="agentType">Optional specific agent type to spawn for the subtask</param>
        /// <param name="cancellationToken">Token for cancelling the operation</param>
        /// <returns>A task containing the ID of the spawned child agent</returns>
        Task<string> AssignSubtaskAsync(string subtaskPrompt, string? agentType = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Handles the completion of a subtask by a child agent.
        /// Called when a child agent reports that it has finished its assigned work.
        /// </summary>
        /// <param name="childAgentId">The ID of the child agent that completed the subtask</param>
        /// <param name="result">The result or output from the completed subtask</param>
        /// <param name="cancellationToken">Token for cancelling the operation</param>
        /// <returns>A task representing the asynchronous subtask completion handling</returns>
        Task HandleSubtaskCompletionAsync(string childAgentId, object result, CancellationToken cancellationToken = default);

        /// <summary>
        /// Handles the failure of a subtask by a child agent.
        /// Called when a child agent reports that it failed to complete its assigned work.
        /// </summary>
        /// <param name="childAgentId">The ID of the child agent that failed the subtask</param>
        /// <param name="error">The error or exception that caused the failure</param>
        /// <param name="cancellationToken">Token for cancelling the operation</param>
        /// <returns>A task representing the asynchronous subtask failure handling</returns>
        Task HandleSubtaskFailureAsync(string childAgentId, Exception error, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Sets the agent factory for this agent.
        /// This allows the agent to spawn child agents for subtasks.
        /// </summary>
        /// <param name="agentFactory">The agent factory instance</param>
        void SetAgentFactory(IAgentFactory agentFactory);
        
        /// <summary>
        /// Sets the parent agent ID for this agent.
        /// This establishes the agent hierarchy for task decomposition.
        /// </summary>
        /// <param name="parentAgentId">The ID of the parent agent</param>
        void SetParentAgentId(string? parentAgentId);

        /// <summary>
        /// Event raised when the agent's status changes.
        /// Useful for monitoring agent progress and coordinating with parent agents.
        /// </summary>
        event EventHandler<AgentStatusChangedEventArgs>? StatusChanged;

        /// <summary>
        /// Event raised when the agent spawns a new child agent for a subtask.
        /// Provides visibility into the agent hierarchy and task decomposition.
        /// </summary>
        event EventHandler<ChildAgentSpawnedEventArgs>? ChildAgentSpawned;

        /// <summary>
        /// Event raised when a child agent completes its assigned subtask.
        /// Allows for monitoring and coordination of distributed agent work.
        /// </summary>
        event EventHandler<SubtaskCompletedEventArgs>? SubtaskCompleted;
    }

    /// <summary>
    /// Represents the current status of an agent's work on its assigned prompt.
    /// </summary>
    public enum AgentStatus
    {
        /// <summary>
        /// Agent is idle and not currently working on any prompt.
        /// </summary>
        Idle,

        /// <summary>
        /// Agent is actively processing its assigned prompt.
        /// </summary>
        Working,

        /// <summary>
        /// Agent is waiting for child agents to complete their subtasks.
        /// </summary>
        WaitingForSubtasks,

        /// <summary>
        /// Agent is waiting for direct input from a human user.
        /// Added for Human Agent Fallback feature (prd-cli-001.md).
        /// </summary>
        WaitingForHumanInput,

        /// <summary>
        /// Agent has successfully completed its assigned prompt.
        /// </summary>
        Completed,

        /// <summary>
        /// Agent failed to complete its assigned prompt due to an error.
        /// </summary>
        Failed,

        /// <summary>
        /// Agent is processing its assigned prompt.
        /// </summary>
        Processing,

        /// <summary>
        /// Agent is decomposing the task into subtasks.
        /// </summary>
        Decomposing,

        /// <summary>
        /// Agent is executing its assigned prompt.
        /// </summary>
        Executing
    }

    /// <summary>
    /// Event arguments for agent status change events.
    /// </summary>
    public class AgentStatusChangedEventArgs : EventArgs
    {
        public AgentStatus PreviousStatus { get; }
        public AgentStatus NewStatus { get; }
        public string? Reason { get; }
        public DateTimeOffset Timestamp { get; }

        public AgentStatusChangedEventArgs(AgentStatus previousStatus, AgentStatus newStatus, string? reason = null)
        {
            PreviousStatus = previousStatus;
            NewStatus = newStatus;
            Reason = reason;
            Timestamp = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Event arguments for child agent spawned events.
    /// </summary>
    public class ChildAgentSpawnedEventArgs : EventArgs
    {
        public string ParentAgentId { get; }
        public string ChildAgentId { get; }
        public string SubtaskPrompt { get; }
        public string AgentType { get; }
        public DateTimeOffset Timestamp { get; }

        public ChildAgentSpawnedEventArgs(string parentAgentId, string childAgentId, string subtaskPrompt, string agentType)
        {
            ParentAgentId = parentAgentId;
            ChildAgentId = childAgentId;
            SubtaskPrompt = subtaskPrompt;
            AgentType = agentType;
            Timestamp = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Event arguments for subtask completed events.
    /// </summary>
    public class SubtaskCompletedEventArgs : EventArgs
    {
        public string ParentAgentId { get; }
        public string ChildAgentId { get; }
        public object Result { get; }
        public DateTimeOffset Timestamp { get; }

        public SubtaskCompletedEventArgs(string parentAgentId, string childAgentId, object result)
        {
            ParentAgentId = parentAgentId;
            ChildAgentId = childAgentId;
            Result = result;
            Timestamp = DateTimeOffset.UtcNow;
        }
    }
} 