using AgctorSDK.Extensions.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgctorSDK.Extensions.DependencyInjection;

/// <summary>
/// Registers the Agctor background hosted services (TaskScoper and TaskFlow)
/// so that any host application can enable goal-to-task decomposition and
/// task execution with a single call.
/// </summary>
public static class BackgroundServiceExtensions
{
    /// <summary>
    /// Adds <see cref="TaskScoperHostedService"/> and <see cref="TaskFlowHostedService"/>
    /// as hosted background services.  Options are bound from the <c>TaskScoper</c> and
    /// <c>TaskFlow</c> configuration sections when <paramref name="configuration"/> is supplied.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="configuration">
    /// Optional <see cref="IConfiguration"/> used to bind <c>TaskScoper</c> and <c>TaskFlow</c>
    /// option sections.  When <c>null</c>, default intervals (30 s / 10 s) are used.
    /// </param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddAgctorBackgroundServices(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        // TaskScoper — goal → task DAG decomposition
        if (configuration != null)
        {
            services.Configure<TaskScoperHostedService.TaskScoperOptions>(
                configuration.GetSection("TaskScoper"));
        }
        services.AddHostedService<TaskScoperHostedService>();

        // TaskFlow — task DAG execution
        if (configuration != null)
        {
            services.Configure<TaskFlowHostedService.TaskFlowOptions>(
                configuration.GetSection("TaskFlow"));
        }
        services.AddHostedService<TaskFlowHostedService>();

        return services;
    }
}
