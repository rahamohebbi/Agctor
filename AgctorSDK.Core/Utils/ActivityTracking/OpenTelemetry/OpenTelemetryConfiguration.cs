using System;
using AgctorSDK.Core.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace AgctorSDK.Core.Utils.ActivityTracking.OpenTelemetry
{
    /// <summary>
    /// Provides configuration utilities for OpenTelemetry integration.
    /// </summary>
    public static class OpenTelemetryConfiguration
    {
        /// <summary>
        /// Configures OpenTelemetry services with the specified options.
        /// </summary>
        /// <param name="services">The service collection to configure.</param>
        /// <param name="options">The OpenTelemetry configuration options.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection ConfigureOpenTelemetry(
            this IServiceCollection services,
            OpenTelemetryOptions options)
        {
            services.AddOpenTelemetry()
                .WithTracing(builder =>
                {
                    builder
                        .AddSource(options.SourceName)
                        .SetResourceBuilder(ResourceBuilder.CreateDefault()
                            .AddService(serviceName: options.SourceName))
                        .AddConsoleExporter();

                    // Add optional exporters based on configuration
                    if (options.EnableZipkinExporter)
                    {
                        builder.AddZipkinExporter(opts =>
                        {
                            opts.Endpoint = new Uri(options.ZipkinEndpoint);
                        });
                    }

                    if (options.EnableOtlpExporter)
                    {
                        builder.AddOtlpExporter(opts =>
                        {
                            opts.Endpoint = new Uri(options.OtlpEndpoint);
                        });
                    }
                    
                    if (options.EnableJaegerExporter)
                    {
                        builder.AddJaegerExporter(opts =>
                        {
                            if (!string.IsNullOrEmpty(options.JaegerCollectorEndpoint))
                            {
                                // Use HTTP collector endpoint if specified
                                opts.AgentHost = null;
                                opts.AgentPort = 0;
                                opts.Endpoint = new Uri(options.JaegerCollectorEndpoint);
                                Console.WriteLine($"Using Jaeger HTTP collector endpoint: {options.JaegerCollectorEndpoint}");
                            }
                            else
                            {
                                // Use UDP agent endpoint (default)
                                opts.AgentHost = options.JaegerAgentHost;
                                opts.AgentPort = options.JaegerAgentPort;
                                Console.WriteLine($"Using Jaeger UDP agent: {options.JaegerAgentHost}:{options.JaegerAgentPort}");
                            }
                        });
                    }
                });

            // Register a TracerProvider for the ActivityTracker to use
            services.AddSingleton(sp =>
            {
                var tracerFactory = sp.GetRequiredService<TracerProvider>();
                return tracerFactory;
            });

            // Register the OpenTelemetry implementation of IActivityTracker
            services.AddSingleton<IActivityTracker>(sp =>
            {
                var tracerProvider = sp.GetRequiredService<TracerProvider>();
                return new OpenTelemetryActivityTracker(tracerProvider, options.SourceName);
            });

            return services;
        }
    }
} 