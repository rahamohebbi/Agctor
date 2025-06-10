using System;
using Microsoft.Extensions.DependencyInjection;
using AgctorSDK.Core.Utils.Observability.Metrics;
using AgctorSDK.Core.Interfaces;
using Scrutor;

namespace AgctorSDK.Core.Runtime
{
    /// <summary>
    /// Extension methods for configuring runtime services.
    /// </summary>
    public static class RuntimeServiceExtensions
    {
        /// <summary>
        /// Wraps the registered IActorRuntimeAdapter with metrics collection.
        /// </summary>
        /// <param name="services">The service collection</param>
        /// <returns>The service collection for chaining</returns>
        public static IServiceCollection AddActorRuntimeMetrics(this IServiceCollection services)
        {
            // Replace the existing runtime adapter with a metrics-enabled decorator
            services.Decorate<IActorRuntimeAdapter>((inner, provider) => 
                new MetricsEnabledActorRuntimeAdapter(
                    inner, 
                    provider.GetRequiredService<IMetricsCollector>())
            );
            
            return services;
        }
    }
} 