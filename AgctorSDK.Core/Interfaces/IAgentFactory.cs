using System;
using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.Interfaces
{
    /// <summary>
    /// Factory interface for creating and spawning agent instances.
    /// Provides a high-level abstraction over the actor runtime adapter for agent-specific operations.
    /// </summary>
    public interface IAgentFactory
    {
        /// <summary>
        /// Spawns a new agent instance with the specified prompt and configuration.
        /// The agent will be created, initialized, and ready to process the given prompt.
        /// </summary>
        /// <typeparam name="TAgent">The type of agent to spawn (must implement IAgent)</typeparam>
        /// <param name="prompt">The initial prompt or task for the agent to work on</param>
        /// <param name="parentAgentId">Optional ID of the parent agent if this is a child agent</param>
        /// <param name="agentId">Optional specific ID for the new agent (auto-generated if not provided)</param>
        /// <param name="cancellationToken">Token for cancelling the operation</param>
        /// <returns>A task containing the spawned agent instance</returns>
        Task<TAgent> SpawnAgentAsync<TAgent>(string prompt, string? parentAgentId = null, string? agentId = null, CancellationToken cancellationToken = default) 
            where TAgent : class, IAgent;

        /// <summary>
        /// Spawns a new agent instance by type name with the specified prompt and configuration.
        /// Useful when the agent type is determined dynamically at runtime.
        /// </summary>
        /// <param name="agentTypeName">The name of the agent type to spawn</param>
        /// <param name="prompt">The initial prompt or task for the agent to work on</param>
        /// <param name="parentAgentId">Optional ID of the parent agent if this is a child agent</param>
        /// <param name="agentId">Optional specific ID for the new agent (auto-generated if not provided)</param>
        /// <param name="cancellationToken">Token for cancelling the operation</param>
        /// <returns>A task containing the spawned agent instance</returns>
        Task<IAgent> SpawnAgentAsync(string agentTypeName, string prompt, string? parentAgentId = null, string? agentId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets a reference to an existing agent by its ID.
        /// Returns null if the agent doesn't exist or is not accessible.
        /// </summary>
        /// <typeparam name="TAgent">The type of agent to retrieve</typeparam>
        /// <param name="agentId">The unique identifier of the agent</param>
        /// <param name="cancellationToken">Token for cancelling the operation</param>
        /// <returns>A task containing the agent reference or null if not found</returns>
        Task<TAgent?> GetAgentAsync<TAgent>(string agentId, CancellationToken cancellationToken = default) 
            where TAgent : class, IAgent;

        /// <summary>
        /// Gets a reference to an existing agent by its ID without type constraints.
        /// Returns null if the agent doesn't exist or is not accessible.
        /// </summary>
        /// <param name="agentId">The unique identifier of the agent</param>
        /// <param name="cancellationToken">Token for cancelling the operation</param>
        /// <returns>A task containing the agent reference or null if not found</returns>
        Task<IAgent?> GetAgentAsync(string agentId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Stops and removes an agent from the runtime.
        /// The agent will be gracefully shut down and its resources cleaned up.
        /// </summary>
        /// <param name="agentId">The ID of the agent to stop</param>
        /// <param name="cancellationToken">Token for cancelling the operation</param>
        /// <returns>A task representing the asynchronous stop operation</returns>
        Task StopAgentAsync(string agentId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Generates a unique agent ID for new agent instances.
        /// Uses a consistent naming convention for agent identification.
        /// </summary>
        /// <param name="agentTypeName">The type name of the agent</param>
        /// <param name="parentAgentId">Optional parent agent ID for hierarchical naming</param>
        /// <returns>A unique agent ID string</returns>
        string GenerateAgentId(string agentTypeName, string? parentAgentId = null);

        /// <summary>
        /// Gets the underlying actor runtime adapter used by this factory.
        /// Provides access to low-level runtime operations when needed.
        /// </summary>
        IActorRuntimeAdapter RuntimeAdapter { get; }
    }
} 