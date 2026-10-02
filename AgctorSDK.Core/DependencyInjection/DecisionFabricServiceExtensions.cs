using System;
using System.Linq;
using System.Net.Http;
using AgctorSDK.Core.Decisions;
using AgctorSDK.Core.Decisions.Providers;
using AgctorSDK.Core.Utils.Observability.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Core.DependencyInjection
{
    /// <summary>
    /// Registers the decision fabric: providers as plugins, router, service, and telemetry.
    /// Actors still call <see cref="IActorContext.Decide"/>; this only hosts what the decision actor runs.
    /// </summary>
    public static class DecisionFabricServiceExtensions
    {
        public static IServiceCollection AddDecisionFabric(this IServiceCollection services, Action<DecisionFabricOptions>? configure = null)
        {
            if (configure != null)
            {
                services.Configure(configure);
            }

            return Register(services);
        }

        public static IServiceCollection AddDecisionFabric(this IServiceCollection services, IConfiguration section)
        {
            if (section == null)
            {
                throw new ArgumentNullException(nameof(section));
            }

            services.Configure<DecisionFabricOptions>(section);
            return Register(services);
        }

        private static IServiceCollection Register(IServiceCollection services)
        {
            services.AddOptions();
            if (services.Any(descriptor => descriptor.ServiceType == typeof(IDecisionService)))
            {
                return services;
            }

            services.AddHttpClient(LayaDecisionProvider.HttpClientName);
            services.AddHttpClient(OpenAiDecisionProvider.HttpClientName);

            services.AddSingleton<IDecisionProvider>(sp =>
                new RuleDecisionProvider(sp.GetRequiredService<IOptions<DecisionFabricOptions>>().Value.Providers.Rules));

            services.AddSingleton<IDecisionProvider>(sp =>
            {
                var options = sp.GetRequiredService<IOptions<DecisionFabricOptions>>().Value.Providers.Laya;
                var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(LayaDecisionProvider.HttpClientName);
                return new LayaDecisionProvider(http, options);
            });

            services.AddSingleton<IDecisionProvider>(sp =>
            {
                var options = sp.GetRequiredService<IOptions<DecisionFabricOptions>>().Value.Providers.OpenAI;
                var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(OpenAiDecisionProvider.HttpClientName);
                return new OpenAiDecisionProvider(http, options);
            });

            services.TryAddSingleton<IDecisionTelemetry>(sp =>
                new DecisionTelemetry(sp.GetServices<IDecisionEventListener>(), sp.GetService<IMetricsCollector>()));

            services.AddSingleton<IDecisionRouter>(sp =>
                new DecisionRouter(sp.GetServices<IDecisionProvider>(), sp.GetRequiredService<IOptions<DecisionFabricOptions>>().Value));

            services.AddSingleton<IDecisionService>(sp =>
                new DecisionService(
                    sp.GetRequiredService<IDecisionRouter>(),
                    sp.GetRequiredService<IOptions<DecisionFabricOptions>>().Value,
                    sp.GetRequiredService<IDecisionTelemetry>(),
                    sp.GetServices<IDecisionObserver>()));

            return services;
        }
    }
}
