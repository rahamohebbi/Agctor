using System;
using AgctorSDK.Core.Goals;
using Microsoft.Extensions.DependencyInjection;

namespace AgctorSDK.Core.DependencyInjection
{
    /// <summary>
    /// Dependency-injection helpers for the <see cref="IGoalStore"/>.
    /// </summary>
    public static class GoalStoreServiceExtensions
    {
        /// <summary>
        /// Registers a singleton <see cref="InMemoryGoalStore"/> that persists to a file under <c>AppContext.BaseDirectory</c>.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="filePath">Optional custom JSON file path. When <c>null</c> the default <c>goals.json</c> in base directory is used.</param>
        public static IServiceCollection AddInMemoryGoalStore(this IServiceCollection services, string? filePath = null)
        {
            services.AddSingleton<IGoalStore>(_ => new InMemoryGoalStore(filePath));
            return services;
        }
    }
} 