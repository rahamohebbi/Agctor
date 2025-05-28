using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;

namespace AgctorSDK.Core.Agents
{
    /// <summary>
    /// Factory implementation for creating and managing agent instances.
    /// Uses the underlying actor runtime adapter to spawn agents and provides agent-specific functionality.
    /// </summary>
    public class AgentFactory : IAgentFactory
    {
        private readonly IActorRuntimeAdapter _runtimeAdapter;
        private readonly Dictionary<string, Type> _agentTypes;
        private static readonly object _lockObject = new();
        private static int _agentCounter = 0;

        /// <summary>
        /// Gets the underlying actor runtime adapter used by this factory.
        /// </summary>
        public IActorRuntimeAdapter RuntimeAdapter => _runtimeAdapter;

        /// <summary>
        /// Initializes a new instance of the AgentFactory.
        /// </summary>
        /// <param name="runtimeAdapter">The actor runtime adapter to use for spawning agents</param>
        public AgentFactory(IActorRuntimeAdapter runtimeAdapter)
        {
            _runtimeAdapter = runtimeAdapter ?? throw new ArgumentNullException(nameof(runtimeAdapter));
            _agentTypes = new Dictionary<string, Type>();
            
            // Register default agent types
            RegisterDefaultAgentTypes();
        }

        /// <summary>
        /// Spawns a new agent instance with the specified prompt and configuration.
        /// </summary>
        /// <typeparam name="TAgent">The type of agent to spawn</typeparam>
        /// <param name="prompt">The initial prompt for the agent</param>
        /// <param name="parentAgentId">Optional parent agent ID</param>
        /// <param name="agentId">Optional specific agent ID</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The spawned agent instance</returns>
        public async Task<TAgent> SpawnAgentAsync<TAgent>(string prompt, string? parentAgentId = null, string? agentId = null, CancellationToken cancellationToken = default) 
            where TAgent : class, IAgent
        {
            if (string.IsNullOrWhiteSpace(prompt))
                throw new ArgumentException("Prompt cannot be null or empty", nameof(prompt));

            // Generate agent ID if not provided
            agentId ??= GenerateAgentId(typeof(TAgent).Name, parentAgentId);

            // Create initialization data for the agent
            var initData = new AgentInitializationData
            {
                Prompt = prompt,
                ParentAgentId = parentAgentId,
                AgentFactory = this
            };

            // Spawn the agent using the runtime adapter
            var agent = await _runtimeAdapter.SpawnActorAsync<TAgent>(agentId, initData, cancellationToken);

            // Process the initial prompt
            await agent.ProcessPromptAsync(prompt, cancellationToken);

            return agent;
        }

        /// <summary>
        /// Spawns a new agent instance by type name.
        /// </summary>
        /// <param name="agentTypeName">The name of the agent type to spawn</param>
        /// <param name="prompt">The initial prompt for the agent</param>
        /// <param name="parentAgentId">Optional parent agent ID</param>
        /// <param name="agentId">Optional specific agent ID</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The spawned agent instance</returns>
        public async Task<IAgent> SpawnAgentAsync(string agentTypeName, string prompt, string? parentAgentId = null, string? agentId = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(agentTypeName))
                throw new ArgumentException("Agent type name cannot be null or empty", nameof(agentTypeName));

            if (string.IsNullOrWhiteSpace(prompt))
                throw new ArgumentException("Prompt cannot be null or empty", nameof(prompt));

            // Look up the agent type
            if (!_agentTypes.TryGetValue(agentTypeName, out var agentType))
            {
                throw new ArgumentException($"Unknown agent type: {agentTypeName}. Available types: {string.Join(", ", _agentTypes.Keys)}", nameof(agentTypeName));
            }

            // Generate agent ID if not provided
            agentId ??= GenerateAgentId(agentTypeName, parentAgentId);

            // Create initialization data for the agent
            var initData = new AgentInitializationData
            {
                Prompt = prompt,
                ParentAgentId = parentAgentId,
                AgentFactory = this
            };

            // Use reflection to call the generic SpawnActorAsync method
            var method = _runtimeAdapter.GetType().GetMethod(nameof(IActorRuntimeAdapter.SpawnActorAsync));
            var genericMethod = method!.MakeGenericMethod(agentType);
            
            var task = (Task)genericMethod.Invoke(_runtimeAdapter, new object[] { agentId, initData, cancellationToken })!;
            await task;

            // Get the result from the task
            var resultProperty = task.GetType().GetProperty("Result");
            var agent = (IAgent)resultProperty!.GetValue(task)!;

            // Process the initial prompt
            await agent.ProcessPromptAsync(prompt, cancellationToken);

            return agent;
        }

        /// <summary>
        /// Gets a reference to an existing agent by its ID.
        /// </summary>
        /// <typeparam name="TAgent">The type of agent to retrieve</typeparam>
        /// <param name="agentId">The agent ID</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The agent instance or null if not found</returns>
        public async Task<TAgent?> GetAgentAsync<TAgent>(string agentId, CancellationToken cancellationToken = default) 
            where TAgent : class, IAgent
        {
            if (string.IsNullOrWhiteSpace(agentId))
                throw new ArgumentException("Agent ID cannot be null or empty", nameof(agentId));

            return await _runtimeAdapter.GetActorAsync<TAgent>(agentId, cancellationToken);
        }

        /// <summary>
        /// Gets a reference to an existing agent by its ID without type constraints.
        /// </summary>
        /// <param name="agentId">The agent ID</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The agent instance or null if not found</returns>
        public async Task<IAgent?> GetAgentAsync(string agentId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(agentId))
                throw new ArgumentException("Agent ID cannot be null or empty", nameof(agentId));

            return await _runtimeAdapter.GetActorAsync<IAgent>(agentId, cancellationToken);
        }

        /// <summary>
        /// Stops and removes an agent from the runtime.
        /// </summary>
        /// <param name="agentId">The agent ID</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the stop operation</returns>
        public async Task StopAgentAsync(string agentId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(agentId))
                throw new ArgumentException("Agent ID cannot be null or empty", nameof(agentId));

            await _runtimeAdapter.StopActorAsync(agentId, cancellationToken);
        }

        /// <summary>
        /// Generates a unique agent ID for new agent instances.
        /// </summary>
        /// <param name="agentTypeName">The agent type name</param>
        /// <param name="parentAgentId">Optional parent agent ID</param>
        /// <returns>A unique agent ID</returns>
        public string GenerateAgentId(string agentTypeName, string? parentAgentId = null)
        {
            lock (_lockObject)
            {
                _agentCounter++;
                
                var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
                var baseId = $"{agentTypeName}-{timestamp}-{_agentCounter:D4}";
                
                if (!string.IsNullOrWhiteSpace(parentAgentId))
                {
                    return $"{parentAgentId}.{baseId}";
                }
                
                return baseId;
            }
        }

        /// <summary>
        /// Registers an agent type with the factory for dynamic spawning.
        /// </summary>
        /// <typeparam name="TAgent">The agent type to register</typeparam>
        /// <param name="typeName">Optional custom type name (uses class name if not provided)</param>
        public void RegisterAgentType<TAgent>(string? typeName = null) where TAgent : class, IAgent
        {
            typeName ??= typeof(TAgent).Name;
            _agentTypes[typeName] = typeof(TAgent);
        }

        /// <summary>
        /// Registers an agent type with the factory for dynamic spawning.
        /// </summary>
        /// <param name="agentType">The agent type to register</param>
        /// <param name="typeName">Optional custom type name (uses class name if not provided)</param>
        public void RegisterAgentType(Type agentType, string? typeName = null)
        {
            if (agentType == null)
                throw new ArgumentNullException(nameof(agentType));

            if (!typeof(IAgent).IsAssignableFrom(agentType))
                throw new ArgumentException($"Type {agentType.Name} must implement IAgent", nameof(agentType));

            typeName ??= agentType.Name;
            _agentTypes[typeName] = agentType;
        }

        /// <summary>
        /// Gets all registered agent type names.
        /// </summary>
        /// <returns>Collection of registered agent type names</returns>
        public IEnumerable<string> GetRegisteredAgentTypes()
        {
            return _agentTypes.Keys;
        }

        /// <summary>
        /// Registers default agent types that are available by default.
        /// </summary>
        private void RegisterDefaultAgentTypes()
        {
            // Register the basic Agent type
            RegisterAgentType<Agent>("Agent");
            RegisterAgentType<Agent>("BasicAgent");
        }
    }

    /// <summary>
    /// Initialization data passed to agents when they are spawned.
    /// Contains the initial prompt, parent information, and factory reference.
    /// </summary>
    public class AgentInitializationData
    {
        /// <summary>
        /// The initial prompt for the agent to process.
        /// </summary>
        public string Prompt { get; set; } = string.Empty;

        /// <summary>
        /// ID of the parent agent if this is a child agent.
        /// </summary>
        public string? ParentAgentId { get; set; }

        /// <summary>
        /// Reference to the agent factory for spawning child agents.
        /// </summary>
        public IAgentFactory? AgentFactory { get; set; }
    }
} 