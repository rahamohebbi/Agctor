using System;
using AgctorSDK.Core.Utils.Observability.Metrics;
using AgctorSDK.Core.Utils.Observability.Visualization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;

namespace AgctorSDK.Core.DependencyInjection
{
    /// <summary>
    /// Extension methods for registering observability services.
    /// </summary>
    public static class ObservabilityServiceExtensions
    {
        /// <summary>
        /// Adds the Agctor metrics collection to the service collection.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddAgctorMetrics(this IServiceCollection services)
        {
            services.AddSingleton<IMetricsCollector, OpenTelemetryMetricsCollector>();
            return services;
        }
        
        /// <summary>
        /// Adds the Agctor metrics collection to the service collection with a custom implementation.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="implementationFactory">Factory for creating the metrics collector.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddAgctorMetrics(
            this IServiceCollection services,
            Func<IServiceProvider, IMetricsCollector> implementationFactory)
        {
            services.AddSingleton(implementationFactory);
            return services;
        }
        
        /// <summary>
        /// Adds the Agctor visualization services to the service collection.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configureOptions">Action to configure visualization options.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddAgctorVisualization(
            this IServiceCollection services,
            Action<VisualizationOptions>? configureOptions = null)
        {
            // Register default options
            var options = new VisualizationOptions();
            configureOptions?.Invoke(options);
            services.AddSingleton(options);
            
            // Register the visualization service
            services.AddSingleton<IVisualizationService, VisualizationService>();
            
            // Add HttpClient for API calls
            services.AddHttpClient<IVisualizationService, VisualizationService>();
            
            return services;
        }
    }
} 