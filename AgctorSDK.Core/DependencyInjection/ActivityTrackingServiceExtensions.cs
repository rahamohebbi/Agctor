using System;
using System.Collections.Generic;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Tools;
using AgctorSDK.Core.Utils.ActivityTracking;
using AgctorSDK.Core.Utils.ActivityTracking.Logger;
using AgctorSDK.Core.Utils.ActivityTracking.OpenTelemetry;
using AgctorSDK.Core.Utils.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgctorSDK.Core.DependencyInjection
{
    /// <summary>
    /// Extensions methods for registering activity tracking services with the dependency injection container.
    /// </summary>
    public static class ActivityTrackingServiceExtensions
    {
        /// <summary>
        /// Adds activity tracking services to the service collection.
        /// </summary>
        /// <param name="services">The service collection to add services to.</param>
        /// <param name="configureOptions">Optional action to configure activity tracking options.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddAgctorActivityTracking(
            this IServiceCollection services,
            Action<ActivityTrackingOptions>? configureOptions = null)
        {
            // Configure options
            var options = new ActivityTrackingOptions();
            configureOptions?.Invoke(options);

            // Register the default logger-based tracker if no custom type is specified
            if (options.CustomActivityTrackerType != null)
            {
                services.AddSingleton(typeof(IActivityTracker), options.CustomActivityTrackerType);
            }
            else
            {
                services.AddSingleton<IActivityTracker>(sp => 
                    new LoggerActivityTracker(sp.GetRequiredService<IAgctorLogger>()));
            }

            // Decorate the agent factory
            services.Decorate<IAgentFactory>((inner, sp) =>
                new TracingAgentFactory(inner, sp.GetRequiredService<IActivityTracker>()));

            // Optionally decorate tool actors
            if (options.EnableToolTracing)
            {
                services.TryAddSingleton<IToolActorDecorator>(sp =>
                    new TracingToolActorDecorator(sp.GetRequiredService<IActivityTracker>()));
            }

            return services;
        }

        /// <summary>
        /// Adds OpenTelemetry-based activity tracking. This configures the OpenTelemetry SDK
        /// and registers the appropriate IActivityTracker implementation.
        /// </summary>
        /// <param name="services">The service collection to add services to.</param>
        /// <param name="configureOptions">Optional action to configure OpenTelemetry options.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddAgctorOpenTelemetryTracking(
            this IServiceCollection services,
            Action<OpenTelemetryOptions>? configureOptions = null)
        {
            // Configure options
            var options = new OpenTelemetryOptions();
            configureOptions?.Invoke(options);

            // Configure OpenTelemetry
            services.ConfigureOpenTelemetry(options);

            // Decorate the agent factory
            services.Decorate<IAgentFactory>((inner, sp) =>
                new TracingAgentFactory(inner, sp.GetRequiredService<IActivityTracker>()));

            // Optionally decorate tool actors
            services.TryAddSingleton<IToolActorDecorator>(sp =>
                new TracingToolActorDecorator(sp.GetRequiredService<IActivityTracker>()));

            return services;
        }

        private class TracingToolActorDecorator : IToolActorDecorator
        {
            private readonly IActivityTracker _activityTracker;

            public TracingToolActorDecorator(IActivityTracker activityTracker)
            {
                _activityTracker = activityTracker;
            }

            public IToolActor Decorate(IToolActor toolActor)
            {
                return new TracedToolActor(toolActor, _activityTracker);
            }
        }
    }

    /// <summary>
    /// Options for configuring activity tracking.
    /// </summary>
    public class ActivityTrackingOptions
    {
        /// <summary>
        /// Gets or sets a custom activity tracker implementation type.
        /// </summary>
        public Type? CustomActivityTrackerType { get; set; }

        /// <summary>
        /// Gets or sets whether to enable tracing for tool actors.
        /// </summary>
        public bool EnableToolTracing { get; set; } = true;
    }

    /// <summary>
    /// Options for configuring OpenTelemetry-based activity tracking.
    /// </summary>
    public class OpenTelemetryOptions
    {
        /// <summary>
        /// Gets or sets the name of the trace source.
        /// </summary>
        public string SourceName { get; set; } = "Agctor";

        /// <summary>
        /// Gets or sets whether to export traces to Zipkin.
        /// </summary>
        public bool EnableZipkinExporter { get; set; }

        /// <summary>
        /// Gets or sets the Zipkin endpoint URL.
        /// </summary>
        public string ZipkinEndpoint { get; set; } = "http://localhost:9411/api/v2/spans";

        /// <summary>
        /// Gets or sets whether to export traces using the OTLP protocol.
        /// </summary>
        public bool EnableOtlpExporter { get; set; }

        /// <summary>
        /// Gets or sets the OTLP endpoint URL.
        /// </summary>
        public string OtlpEndpoint { get; set; } = "http://localhost:4317";
        
        /// <summary>
        /// Gets or sets whether to export traces to Jaeger.
        /// </summary>
        public bool EnableJaegerExporter { get; set; }
        
        /// <summary>
        /// Gets or sets the Jaeger agent host.
        /// </summary>
        public string JaegerAgentHost { get; set; } = "localhost";
        
        /// <summary>
        /// Gets or sets the Jaeger agent port.
        /// </summary>
        public int JaegerAgentPort { get; set; } = 6831;
        
        /// <summary>
        /// Gets or sets the Jaeger collector HTTP endpoint URL.
        /// When specified, this will be used instead of the agent host/port.
        /// </summary>
        public string? JaegerCollectorEndpoint { get; set; }
    }

    /// <summary>
    /// Interface for a decorator of tool actors.
    /// </summary>
    public interface IToolActorDecorator
    {
        /// <summary>
        /// Decorates a tool actor with additional functionality.
        /// </summary>
        /// <param name="toolActor">The tool actor to decorate.</param>
        /// <returns>The decorated tool actor.</returns>
        IToolActor Decorate(IToolActor toolActor);
    }
} 