using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Runtime;
using AgctorSDK.Core.Adapters;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Utils.Logging;
using AgctorSDK.Core.Utils.ErrorHandling;
using AgctorSDK.Core.Utils.Observability.Metrics;

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
            
            // Register logging and error handling services
            services.AddSingleton<IAgctorLogger>(sp => 
            {
                var options = sp.GetService<Microsoft.Extensions.Options.IOptions<AgctorOptions>>()?.Value;
                var minLevel = options?.EnableDetailedLogging == true ? LogLevel.Trace : LogLevel.Info;
                return LoggerFactory.CreateLogger("Agctor", minLevel);
            });
            
            services.AddSingleton<ErrorHandlingMiddleware>();
            
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
            
            // Register logging and error handling services
            services.AddSingleton<IAgctorLogger>(sp =>
            {
                var options = sp.GetService<Microsoft.Extensions.Options.IOptions<AgctorOptions>>()?.Value;
                var minLevel = options?.EnableDetailedLogging == true ? LogLevel.Trace : LogLevel.Info;
                return LoggerFactory.CreateLogger("Agctor", minLevel);
            });
            
            services.AddSingleton<ErrorHandlingMiddleware>();
            
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

        /// <summary>
        /// Decorates a registered service with a decorator of the same service type.
        /// </summary>
        /// <typeparam name="TService">The type of the service being decorated.</typeparam>
        /// <param name="services">The service collection.</param>
        /// <param name="decorator">A function that creates the decorator using the original service and service provider.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection Decorate<TService>(
            this IServiceCollection services,
            Func<TService, IServiceProvider, TService> decorator)
            where TService : class
        {
            // Find the existing registration
            var serviceDescriptor = services.FindServiceDescriptor<TService>();
            if (serviceDescriptor == null)
            {
                throw new InvalidOperationException($"Service of type {typeof(TService).Name} is not registered.");
            }

            // Create a new descriptor with the decorator
            var decoratedDescriptor = new ServiceDescriptor(
                serviceDescriptor.ServiceType,
                sp =>
                {
                    // Resolve the original service
                    var original = GetOriginalService<TService>(sp, serviceDescriptor);
                    // Apply the decorator
                    return decorator(original, sp);
                },
                serviceDescriptor.Lifetime);

            // Replace the original registration with the decorated one
            services.Remove(serviceDescriptor);
            services.Add(decoratedDescriptor);

            return services;
        }

        /// <summary>
        /// Finds the first service descriptor for the specified service type.
        /// </summary>
        private static ServiceDescriptor? FindServiceDescriptor<TService>(this IServiceCollection services)
            where TService : class
        {
            foreach (var descriptor in services)
            {
                if (descriptor.ServiceType == typeof(TService))
                {
                    return descriptor;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the original service instance from the service descriptor.
        /// </summary>
        private static TService GetOriginalService<TService>(IServiceProvider serviceProvider, ServiceDescriptor descriptor)
            where TService : class
        {
            // Handle different kinds of registrations
            if (descriptor.ImplementationInstance != null)
            {
                return (TService)descriptor.ImplementationInstance;
            }

            if (descriptor.ImplementationFactory != null)
            {
                return (TService)descriptor.ImplementationFactory(serviceProvider);
            }

            if (descriptor.ImplementationType != null)
            {
                return (TService)ActivatorUtilities.CreateInstance(serviceProvider, descriptor.ImplementationType);
            }

            throw new InvalidOperationException("Could not get the original service instance.");
        }

        /// <summary>
        /// Enables metrics collection for the Agctor system.
        /// Registers the necessary services and decorators for collecting system metrics.
        /// </summary>
        /// <param name="services">The service collection to add services to</param>
        /// <param name="meterName">Optional custom meter name for the metrics collector</param>
        /// <returns>The service collection for method chaining</returns>
        public static IServiceCollection AddAgctorWithMetrics(this IServiceCollection services, string meterName = "AgctorSDK.Core")
        {
            // Add the base Agctor services
            services.AddAgctor();
            
            // Add metrics collection
            services.AddAgctorMetrics(meterName);
            
            // Enable runtime metrics
            services.AddActorRuntimeMetrics();
            
            return services;
        }
        
        /// <summary>
        /// Enables metrics collection for a specific Agctor runtime.
        /// </summary>
        /// <typeparam name="TAdapter">The type of adapter to use as default</typeparam>
        /// <param name="services">The service collection to add services to</param>
        /// <param name="meterName">Optional custom meter name for the metrics collector</param>
        /// <returns>The service collection for method chaining</returns>
        public static IServiceCollection AddAgctorWithMetrics<TAdapter>(this IServiceCollection services, string meterName = "AgctorSDK.Core")
            where TAdapter : class, IActorRuntimeAdapter
        {
            // Add the base Agctor services with the specified adapter
            services.AddAgctor<TAdapter>();
            
            // Add metrics collection
            services.AddAgctorMetrics(meterName);
            
            // Enable runtime metrics
            services.AddActorRuntimeMetrics();
            
            return services;
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