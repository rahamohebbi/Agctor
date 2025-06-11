using System.Collections.Generic;
using System.Threading.Tasks;

namespace AgctorSDK.Core.Interfaces
{
    /// <summary>
    /// Interface for a registry that tracks and provides access to agent instances in the system.
    /// </summary>
    public interface IAgentRegistry
    {
        /// <summary>
        /// Gets an agent by its ID.
        /// </summary>
        /// <typeparam name="T">The type of agent to retrieve</typeparam>
        /// <param name="agentId">The unique identifier of the agent</param>
        /// <returns>The agent instance if found, or null if not found</returns>
        Task<T?> GetAgentAsync<T>(string agentId) where T : class, IAgent;
        
        /// <summary>
        /// Gets all registered agents.
        /// </summary>
        /// <returns>A collection of all registered agent IDs</returns>
        Task<IEnumerable<string>> GetAllAgentIdsAsync();
        
        /// <summary>
        /// Gets all root agents (agents without a parent).
        /// </summary>
        /// <returns>A collection of root agent IDs</returns>
        Task<IEnumerable<string>> GetRootAgentIdsAsync();
        
        /// <summary>
        /// Registers an agent with the registry.
        /// </summary>
        /// <param name="agent">The agent to register</param>
        /// <returns>A task representing the asynchronous operation</returns>
        Task RegisterAgentAsync(IAgent agent);
        
        /// <summary>
        /// Unregisters an agent from the registry.
        /// </summary>
        /// <param name="agentId">The ID of the agent to unregister</param>
        /// <returns>A task representing the asynchronous operation</returns>
        Task UnregisterAgentAsync(string agentId);
        
        /// <summary>
        /// Gets an agent by its ID.
        /// </summary>
        /// <param name="agentId">The ID of the agent to retrieve.</param>
        /// <returns>The agent if found, or null if not found.</returns>
        Task<IAgent?> GetAgentByIdAsync(string agentId);
        
        /// <summary>
        /// Gets all registered agents.
        /// </summary>
        /// <returns>Collection of all registered agents.</returns>
        Task<IEnumerable<IAgent>> GetAllAgentsAsync();
    }
} 