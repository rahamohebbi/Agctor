using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AgctorSDK.Core.DependencyInjection
{
    /// <summary>
    /// Starts the actor runtime with the host so library apps do not have to call
    /// <see cref="IActorRuntimeAdapter.InitializeAsync"/> by hand.
    /// </summary>
    public sealed class ActorRuntimeInitializer : IHostedService
    {
        private readonly IActorRuntimeAdapter _runtime;

        public ActorRuntimeInitializer(IActorRuntimeAdapter runtime)
        {
            _runtime = runtime;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (_runtime.IsInitialized)
            {
                return Task.CompletedTask;
            }

            return _runtime.InitializeAsync(new Dictionary<string, object>(), cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            if (!_runtime.IsInitialized)
            {
                return Task.CompletedTask;
            }

            return _runtime.ShutdownAsync(cancellationToken);
        }
    }

    public static class ActorRuntimeHostExtensions
    {
        /// <summary>
        /// Registers <see cref="ActorRuntimeInitializer"/>. Call this after <c>AddAgctor()</c>.
        /// </summary>
        public static IServiceCollection AddAgctorHostedRuntime(this IServiceCollection services)
        {
            services.AddHostedService<ActorRuntimeInitializer>();
            return services;
        }
    }
}
