using System;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AgctorSDK.Core.DependencyInjection
{
    /// <summary>
    /// Extension methods for registering agent-related services with the dependency injection container.
    /// </summary>
    public static class AgentServiceExtensions
    {
        /// <summary>
        /// Adds the agent factory to the service collection.
        /// </summary>
        /// <param name="services">The service collection to add the agent factory to.</param>
        /// <returns>The service collection for method chaining.</returns>
        public static IServiceCollection AddAgentFactory(this IServiceCollection services)
        {
            services.AddSingleton<IAgentFactory, AgentFactory>();
            return services;
        }

        /// <summary>
        /// Adds the agent registry to the service collection.
        /// </summary>
        /// <param name="services">The service collection to add the agent registry to.</param>
        /// <returns>The service collection for method chaining.</returns>
        public static IServiceCollection AddAgentRegistry(this IServiceCollection services)
        {
            services.AddSingleton<IAgentRegistry, AgentRegistry>();
            return services;
        }

        /// <summary>
        /// Registers a specific agent type with the service collection.
        /// </summary>
        /// <typeparam name="TAgent">The type of agent to register.</typeparam>
        /// <param name="services">The service collection to register the agent type with.</param>
        /// <param name="agentTypeName">Optional custom type name for the agent.</param>
        /// <returns>The service collection for method chaining.</returns>
        public static IServiceCollection AddAgentType<TAgent>(
            this IServiceCollection services,
            string? agentTypeName = null) where TAgent : class, IAgent
        {
            agentTypeName ??= typeof(TAgent).Name;
            
            services.AddTransient<TAgent>();
            services.Configure<AgentTypeOptions>(options =>
            {
                options.RegisterAgentType(agentTypeName, typeof(TAgent));
            });
            
            return services;
        }
    }
} 