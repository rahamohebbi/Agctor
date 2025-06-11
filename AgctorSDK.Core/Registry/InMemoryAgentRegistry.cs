using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;

namespace AgctorSDK.Core.Registry
{
    /// <summary>
    /// In-memory implementation of the agent registry.
    /// </summary>
    public class InMemoryAgentRegistry : IAgentRegistry
    {
        private readonly ConcurrentDictionary<string, IAgent> _agents = new();

        /// <summary>
        /// Gets an agent by its ID.
        /// </summary>
        public Task<T?> GetAgentAsync<T>(string agentId) where T : class, IAgent
        {
            if (_agents.TryGetValue(agentId, out var agent) && agent is T typedAgent)
            {
                return Task.FromResult<T?>(typedAgent);
            }
            return Task.FromResult<T?>(null);
        }

        /// <summary>
        /// Gets all registered agent IDs.
        /// </summary>
        public Task<IEnumerable<string>> GetAllAgentIdsAsync()
        {
            return Task.FromResult<IEnumerable<string>>(_agents.Keys);
        }

        /// <summary>
        /// Gets all root agents (agents without a parent).
        /// </summary>
        public Task<IEnumerable<string>> GetRootAgentIdsAsync()
        {
            var rootAgentIds = _agents.Values
                .Where(agent => string.IsNullOrEmpty(agent.ParentAgentId))
                .Select(agent => agent.Id);
            
            return Task.FromResult(rootAgentIds);
        }

        /// <summary>
        /// Registers an agent with the registry.
        /// </summary>
        public Task RegisterAgentAsync(IAgent agent)
        {
            _agents[agent.Id] = agent;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Unregisters an agent from the registry.
        /// </summary>
        public Task UnregisterAgentAsync(string agentId)
        {
            _agents.TryRemove(agentId, out _);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Gets an agent by its ID.
        /// </summary>
        public Task<IAgent?> GetAgentByIdAsync(string agentId)
        {
            if (_agents.TryGetValue(agentId, out var agent))
            {
                return Task.FromResult<IAgent?>(agent);
            }
            return Task.FromResult<IAgent?>(null);
        }

        /// <summary>
        /// Gets all registered agents.
        /// </summary>
        public Task<IEnumerable<IAgent>> GetAllAgentsAsync()
        {
            return Task.FromResult<IEnumerable<IAgent>>(_agents.Values);
        }
    }
} 