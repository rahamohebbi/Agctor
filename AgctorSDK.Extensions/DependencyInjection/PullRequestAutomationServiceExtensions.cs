using System;
using System.Linq;
using AgctorSDK.Agents.Tasks;
using AgctorSDK.Core.Git;
using AgctorSDK.Core.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace AgctorSDK.Extensions.DependencyInjection;

/// <summary>
/// Registers Git + PullRequest automation services so the TaskFlowEngine can
/// create pull-requests at the end of a goal workflow.
/// </summary>
public static class PullRequestAutomationServiceExtensions
{
    /// <summary>
    /// Adds <see cref="IGitService"/>, <see cref="PullRequestTaskExecutor"/> (as the primary <see cref="ITaskExecutor"/>)
    /// and its fallback <see cref="CodeGraphTaskExecutor"/> to the DI container.
    /// </summary>
    public static IServiceCollection AddPullRequestAutomation(this IServiceCollection services)
    {
        // Git CLI wrapper
        services.AddSingleton<IGitService, AgctorSDK.Core.Git.GitCliService>();

        // Support code-generation tasks via CodeGraph
        services.AddSingleton<CodeGraphTaskExecutor>();

        // Primary executor – delegates to CodeGraphTaskExecutor when task is not PR-related.
        services.AddSingleton<ITaskExecutor>(sp =>
        {
            var registry = sp.GetRequiredService<AgctorSDK.Core.Interfaces.IAgentRegistry>();
            var factory = sp.GetRequiredService<AgctorSDK.Core.Interfaces.IAgentFactory>();
            var git = sp.GetRequiredService<IGitService>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PullRequestTaskExecutor>>();
            var agentLogger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AgctorSDK.Agents.Agents.PullRequestAgent>>();
            var fallback = sp.GetRequiredService<CodeGraphTaskExecutor>();
            return new PullRequestTaskExecutor(registry, factory, git, logger, agentLogger, fallback);
        });

        return services;
    }
} 