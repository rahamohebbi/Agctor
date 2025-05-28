using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Runtime;
using AgctorSDK.Core.Adapters;
using AgctorSDK.Core.Agents;

namespace AgctorSDK.Core.DependencyInjection
{
    /// <summary>
    /// Extension methods for configuring Agctor services in the dependency injection container.
    /// Provides fluent API for registering actor runtime adapters and configuring the actor system.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds Agctor services to the dependency injection container with InMemoryActorRuntime as default.
        /// This is the recommended way to configure Agctor for development and testing scenarios.
        /// </summary>
        /// <param name="services">The service collection to add services to</param>
        /// <param name="configureOptions">Optional configuration action for runtime settings</param>
        /// <returns>The service collection for method chaining</returns>
        public static IServiceCollection AddAgctor(this IServiceCollection services, Action<AgctorOptions>? configureOptions = null)
        {
            // Register the default InMemoryActorRuntime as the primary adapter
            services.TryAddSingleton<IActorRuntimeAdapter, InMemoryActorRuntime>();
            
            // Register all available adapters as named services for factory pattern
            services.AddSingleton<InMemoryActorRuntime>();
            services.AddSingleton<OrleansAdapter>();
            services.AddSingleton<ProtoActorAdapter>();
            
            // Register the adapter factory for runtime switching
            services.AddSingleton<IActorRuntimeAdapterFactory, ActorRuntimeAdapterFactory>();
            
            // Register the agent factory for agent functionality
            services.AddSingleton<IAgentFactory, AgentFactory>();
            
            // Configure options if provided
            if (configureOptions != null)
            {
                services.Configure(configureOptions);
            }
            
            return services;
        }

        /// <summary>
        /// Adds Agctor services with a specific runtime adapter type.
        /// Use this method when you want to explicitly specify which adapter to use as default.
        /// </summary>
        /// <typeparam name="TAdapter">The type of adapter to use as default</typeparam>
        /// <param name="services">The service collection to add services to</param>
        /// <param name="configureOptions">Optional configuration action for runtime settings</param>
        /// <returns>The service collection for method chaining</returns>
        public static IServiceCollection AddAgctor<TAdapter>(this IServiceCollection services, Action<AgctorOptions>? configureOptions = null)
            where TAdapter : class, IActorRuntimeAdapter
        {
            // Register the specified adapter as the primary implementation
            services.TryAddSingleton<IActorRuntimeAdapter, TAdapter>();
            
            // Register all available adapters as named services
            services.AddSingleton<InMemoryActorRuntime>();
            services.AddSingleton<OrleansAdapter>();
            services.AddSingleton<ProtoActorAdapter>();
            
            // Register the adapter factory for runtime switching
            services.AddSingleton<IActorRuntimeAdapterFactory, ActorRuntimeAdapterFactory>();
            
            // Register the agent factory for agent functionality
            services.AddSingleton<IAgentFactory, AgentFactory>();
            
            // Configure options if provided
            if (configureOptions != null)
            {
                services.Configure(configureOptions);
            }
            
            return services;
        }

        /// <summary>
        /// Adds Agctor services with InMemoryActorRuntime explicitly configured.
        /// This method provides additional configuration options specific to the in-memory runtime.
        /// </summary>
        /// <param name="services">The service collection to add services to</param>
        /// <param name="configureOptions">Optional configuration action for runtime settings</param>
        /// <returns>The service collection for method chaining</returns>
        public static IServiceCollection AddAgctorInMemory(this IServiceCollection services, Action<AgctorOptions>? configureOptions = null)
        {
            return services.AddAgctor<InMemoryActorRuntime>(configureOptions);
        }

        /// <summary>
        /// Adds Agctor services with Orleans adapter configured (placeholder).
        /// Note: Orleans adapter is not yet implemented and will throw NotImplementedException.
        /// </summary>
        /// <param name="services">The service collection to add services to</param>
        /// <param name="configureOptions">Optional configuration action for runtime settings</param>
        /// <returns>The service collection for method chaining</returns>
        public static IServiceCollection AddAgctorOrleans(this IServiceCollection services, Action<AgctorOptions>? configureOptions = null)
        {
            return services.AddAgctor<OrleansAdapter>(configureOptions);
        }

        /// <summary>
        /// Adds Agctor services with Proto.Actor adapter configured (placeholder).
        /// Note: Proto.Actor adapter is not yet implemented and will throw NotImplementedException.
        /// </summary>
        /// <param name="services">The service collection to add services to</param>
        /// <param name="configureOptions">Optional configuration action for runtime settings</param>
        /// <returns>The service collection for method chaining</returns>
        public static IServiceCollection AddAgctorProtoActor(this IServiceCollection services, Action<AgctorOptions>? configureOptions = null)
        {
            return services.AddAgctor<ProtoActorAdapter>(configureOptions);
        }
    }

    /// <summary>
    /// Configuration options for the Agctor actor system.
    /// Provides settings that can be applied across different runtime adapters.
    /// </summary>
    public class AgctorOptions
    {
        /// <summary>
        /// The default runtime adapter to use when multiple are available.
        /// Valid values: "InMemory", "Orleans", "Proto.Actor"
        /// </summary>
        public string DefaultRuntime { get; set; } = "InMemory";

        /// <summary>
        /// Maximum number of concurrent messages that can be processed.
        /// This setting may be interpreted differently by each runtime adapter.
        /// </summary>
        public int MaxConcurrentMessages { get; set; } = 1000;

        /// <summary>
        /// Default timeout for request-response operations in milliseconds.
        /// </summary>
        public int DefaultTimeoutMs { get; set; } = 30000;

        /// <summary>
        /// Whether to enable detailed logging and tracing for actor operations.
        /// </summary>
        public bool EnableDetailedLogging { get; set; } = false;

        /// <summary>
        /// Environment name for the actor system (e.g., "Development", "Production").
        /// </summary>
        public string Environment { get; set; } = "Development";

        /// <summary>
        /// Additional runtime-specific configuration properties.
        /// These will be passed to the adapter during initialization.
        /// </summary>
        public Dictionary<string, object> AdditionalProperties { get; set; } = new();
    }
} 