using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Utils.Logging;

namespace AgctorSDK.Core.Agents
{
    /// <summary>
    /// Default implementation of the agent registry that keeps track of all agent instances.
    /// </summary>
    public class AgentRegistry : IAgentRegistry
    {
        private readonly ConcurrentDictionary<string, IAgent> _agents = new();
        private readonly IAgctorLogger _logger;

        /// <summary>
        /// Initializes a new instance of the AgentRegistry class.
        /// </summary>
        /// <param name="logger">Logger for diagnostic output</param>
        public AgentRegistry(IAgctorLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public Task<T?> GetAgentAsync<T>(string agentId) where T : class, IAgent
        {
            if (string.IsNullOrEmpty(agentId))
            {
                throw new ArgumentException("Agent ID cannot be null or empty", nameof(agentId));
            }

            if (_agents.TryGetValue(agentId, out var agent) && agent is T typedAgent)
            {
                return Task.FromResult<T?>(typedAgent);
            }

            _logger.Warning($"Agent with ID {agentId} not found or not of requested type {typeof(T).Name}");
            return Task.FromResult<T?>(null);
        }

        /// <inheritdoc />
        public Task<IEnumerable<string>> GetAllAgentIdsAsync()
        {
            return Task.FromResult<IEnumerable<string>>(_agents.Keys.ToList());
        }

        /// <inheritdoc />
        public Task<IEnumerable<string>> GetRootAgentIdsAsync()
        {
            var rootAgents = _agents.Values
                .Where(a => string.IsNullOrEmpty(a.ParentAgentId))
                .Select(a => a.Id)
                .ToList();
                
            return Task.FromResult<IEnumerable<string>>(rootAgents);
        }

        /// <inheritdoc />
        public Task RegisterAgentAsync(IAgent agent)
        {
            if (agent == null)
            {
                throw new ArgumentNullException(nameof(agent));
            }

            if (string.IsNullOrEmpty(agent.Id))
            {
                throw new ArgumentException("Agent must have a valid ID", nameof(agent));
            }

            if (_agents.TryAdd(agent.Id, agent))
            {
                _logger.Info($"Registered agent {agent.Id} of type {agent.ActorType}");
            }
            else
            {
                _logger.Warning($"Agent with ID {agent.Id} already registered");
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task UnregisterAgentAsync(string agentId)
        {
            if (string.IsNullOrEmpty(agentId))
            {
                throw new ArgumentException("Agent ID cannot be null or empty", nameof(agentId));
            }

            if (_agents.TryRemove(agentId, out _))
            {
                _logger.Info($"Unregistered agent {agentId}");
            }
            else
            {
                _logger.Warning($"Failed to unregister agent {agentId}: agent not found");
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<IAgent?> GetAgentByIdAsync(string agentId)
        {
            if (string.IsNullOrEmpty(agentId))
            {
                throw new ArgumentException("Agent ID cannot be null or empty", nameof(agentId));
            }

            if (_agents.TryGetValue(agentId, out var agent))
            {
                return Task.FromResult<IAgent?>(agent);
            }

            _logger.Warning($"Agent with ID {agentId} not found");
            return Task.FromResult<IAgent?>(null);
        }

        /// <inheritdoc />
        public Task<IEnumerable<IAgent>> GetAllAgentsAsync()
        {
            return Task.FromResult<IEnumerable<IAgent>>(_agents.Values.ToList());
        }
    }
} 