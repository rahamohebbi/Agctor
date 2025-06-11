using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Utils.Logging;

namespace AgctorSDK.Core.DependencyInjection
{
    /// <summary>
    /// Extension methods for registering core Agctor services with the dependency injection container.
    /// </summary>
    public static class CoreServiceExtensions
    {
        /// <summary>
        /// Adds core Agctor services to the service collection.
        /// </summary>
        /// <param name="services">The service collection to add the services to.</param>
        /// <returns>The service collection for method chaining.</returns>
        public static IServiceCollection AddAgctorCore(this IServiceCollection services)
        {
            // Add required services
            services.TryAddSingleton<IAgctorLogger>(LoggerFactory.CreateLogger("Agctor"));
            
            // Add agent services
            services.AddAgentFactory();
            services.AddAgentRegistry();
            services.AddAgentType<Agent>();
            
            // Add utility services
            services.AddAgctorMetrics();
            
            return services;
        }
        
        /// <summary>
        /// Adds a default runtime implementation to the service collection.
        /// </summary>
        /// <param name="services">The service collection to add the runtime to.</param>
        /// <returns>The service collection for method chaining.</returns>
        public static IServiceCollection AddDefaultRuntime(this IServiceCollection services)
        {
            // Add default runtime implementation
            // This would be implemented in a real system
            
            return services;
        }
        
        /// <summary>
        /// Adds observability services to the service collection.
        /// </summary>
        /// <param name="services">The service collection to add the services to.</param>
        /// <returns>The service collection for method chaining.</returns>
        public static IServiceCollection AddAgctorObservability(this IServiceCollection services)
        {
            // Add metrics
            services.AddAgctorMetrics();
            
            // Add activity tracking
            services.AddAgctorActivityTracking();
            
            // Add visualization
            services.AddAgctorVisualization();
            
            return services;
        }
    }
} 