using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Utils.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Core.Agents
{
    /// <summary>
    /// Default implementation of the agent factory for creating agent instances.
    /// </summary>
    public class AgentFactory : IAgentFactory
    {
        private readonly IActorRuntimeAdapter _runtimeAdapter;
        private readonly Dictionary<string, Type> _agentTypes;
        private static readonly object _lockObject = new();
        private static int _agentCounter = 0;
        private readonly IServiceProvider _serviceProvider;
        private readonly IAgctorLogger _logger;
        private readonly IAgentRegistry _agentRegistry;

        /// <summary>
        /// Initializes a new instance of the AgentFactory class.
        /// </summary>
        /// <param name="runtimeAdapter">The actor runtime adapter to use for spawning agents</param>
        /// <param name="serviceProvider">The service provider for dependency resolution</param>
        /// <param name="logger">Logger for diagnostic information</param>
        /// <param name="agentRegistry">Registry for tracking agent instances</param>
        /// <param name="options">Optional agent type options</param>
        public AgentFactory(
            IActorRuntimeAdapter runtimeAdapter,
            IServiceProvider serviceProvider,
            IAgctorLogger logger,
            IAgentRegistry agentRegistry,
            IOptions<AgentTypeOptions>? options = null)
        {
            _runtimeAdapter = runtimeAdapter ?? throw new ArgumentNullException(nameof(runtimeAdapter));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _agentRegistry = agentRegistry ?? throw new ArgumentNullException(nameof(agentRegistry));
            _agentTypes = new Dictionary<string, Type>();
            
            // Register default agent types
            RegisterAgentType<Agent>();
            
            // Register agent types from options if provided
            if (options?.Value != null)
            {
                foreach (var (typeName, agentType) in options.Value.AgentTypes)
                {
                    _agentTypes[typeName] = agentType;
                }
            }
        }

        /// <summary>
        /// Gets the underlying actor runtime adapter used by this factory.
        /// </summary>
        public IActorRuntimeAdapter RuntimeAdapter => _runtimeAdapter;

        /// <summary>
        /// Stops an agent by ID.
        /// </summary>
        /// <param name="agentId">The ID of the agent to stop</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>A task representing the asynchronous operation</returns>
        public Task StopAgentAsync(string agentId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(agentId))
            {
                throw new ArgumentException("Agent ID cannot be null or empty", nameof(agentId));
            }

            _logger.Info($"Stopping agent {agentId}");
            return _runtimeAdapter.StopActorAsync(agentId, cancellationToken);
        }

        /// <summary>
        /// Gets the available agent types registered with this factory.
        /// </summary>
        /// <returns>Collection of registered agent type names</returns>
        public IEnumerable<string> GetAvailableAgentTypes()
        {
            return _agentTypes.Keys;
        }

        /// <summary>
        /// Registers an agent type with the factory.
        /// </summary>
        /// <typeparam name="T">The agent type to register</typeparam>
        /// <param name="typeName">Optional custom type name</param>
        public void RegisterAgentType<T>(string? typeName = null) where T : IAgent
        {
            typeName ??= typeof(T).Name;
            _agentTypes[typeName] = typeof(T);
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

        /// <inheritdoc />
        public async Task<IAgent> SpawnAgentAsync(
            string agentType,
            string? initialPrompt = null,
            string? parentAgentId = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(agentType))
            {
                throw new ArgumentException("Agent type cannot be null or empty", nameof(agentType));
            }

            _logger.Info($"Spawning agent of type {agentType}");

            // Generate a unique agent ID
            string agentId = GenerateAgentId(agentType, parentAgentId);
            
            // Get the agent type
            if (!_agentTypes.TryGetValue(agentType, out var type))
            {
                throw new InvalidOperationException($"Agent type {agentType} not registered");
            }

            try
            {
                // Create the agent instance
                var agent = CreateAgentInstance(type, agentId);

                // Configure the agent
                agent.SetAgentFactory(this);
                agent.SetParentAgentId(parentAgentId);

                // Register the agent with the runtime
                await _runtimeAdapter.RegisterActorAsync(agent, cancellationToken);
                
                // Register the agent with our registry
                await _agentRegistry.RegisterAgentAsync(agent);

                // Initialize the agent
                await agent.InitializeAsync(cancellationToken);

                // Process the initial prompt if provided
                if (!string.IsNullOrEmpty(initialPrompt))
                {
                    await agent.ProcessPromptAsync(initialPrompt, cancellationToken);
                }

                return agent;
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to spawn agent of type {agentType}: {ex.Message}");
                throw;
            }
        }

        private IAgent CreateAgentInstance(Type agentType, string agentId)
        {
            // Try to find a constructor with an ID parameter
            var constructorWithId = agentType.GetConstructor(new[] { typeof(string) });
            if (constructorWithId != null)
            {
                return (IAgent)Activator.CreateInstance(agentType, agentId)!;
            }

            // Try to create using the default constructor and set the ID via reflection
            var agent = (IAgent)ActivatorUtilities.CreateInstance(_serviceProvider, agentType);
            var idProperty = agentType.GetProperty("Id");
            if (idProperty != null && idProperty.CanWrite)
            {
                idProperty.SetValue(agent, agentId);
            }
            else
            {
                _logger.Warning($"Could not set ID for agent of type {agentType.Name}");
            }

            return agent;
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
        public virtual async Task<IAgent> SpawnAgentAsync(string agentTypeName, string prompt, string? parentAgentId = null, string? agentId = null, CancellationToken cancellationToken = default)
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
            var method = _runtimeAdapter.GetType().GetMethod(nameof(IActorRuntimeAdapter.SpawnActorAsync), new[] { typeof(string), typeof(object), typeof(CancellationToken) });
            var genericMethod = method!.MakeGenericMethod(agentType);
            
            var task = (Task)genericMethod.Invoke(_runtimeAdapter, new object?[] { agentId, initData, cancellationToken })!;
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