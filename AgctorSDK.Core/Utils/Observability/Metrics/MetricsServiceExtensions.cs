using System;
using Microsoft.Extensions.DependencyInjection;

namespace AgctorSDK.Core.Utils.Observability.Metrics
{
    /// <summary>
    /// Extension methods for registering metrics services with the dependency injection container.
    /// </summary>
    public static class MetricsServiceExtensions
    {
        /// <summary>
        /// Adds Agctor metrics collection services to the service collection with OpenTelemetry implementation.
        /// </summary>
        /// <param name="services">The service collection to add services to</param>
        /// <param name="meterName">Optional custom meter name</param>
        /// <returns>The service collection for chaining</returns>
        public static IServiceCollection AddAgctorMetrics(
            this IServiceCollection services,
            string meterName = "AgctorSDK.Core")
        {
            services.AddSingleton<IMetricsCollector>(provider => 
                new OpenTelemetryMetricsCollector(meterName));
            
            return services;
        }
        
        /// <summary>
        /// Adds Agctor metrics collection with a no-op implementation that doesn't collect any metrics.
        /// </summary>
        /// <param name="services">The service collection to add services to</param>
        /// <returns>The service collection for chaining</returns>
        public static IServiceCollection AddAgctorNoOpMetrics(this IServiceCollection services)
        {
            services.AddSingleton<IMetricsCollector>(NoOpMetricsCollector.Instance);
            return services;
        }
        
        /// <summary>
        /// Adds Agctor metrics collection with a custom implementation.
        /// </summary>
        /// <param name="services">The service collection to add services to</param>
        /// <param name="implementationFactory">Factory for creating the metrics collector implementation</param>
        /// <returns>The service collection for chaining</returns>
        public static IServiceCollection AddAgctorMetrics(
            this IServiceCollection services,
            Func<IServiceProvider, IMetricsCollector> implementationFactory)
        {
            services.AddSingleton(implementationFactory);
            return services;
        }
    }
} 