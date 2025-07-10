using AgctorSDK.Agents.Tasks;
using AgctorSDK.Core.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace AgctorSDK.Extensions.DependencyInjection;

/// <summary>
/// Registers the CodeGraph-based task executor so the TaskFlowEngine can
/// delegate code-generation tasks to the CodeGraph agent pipeline.
/// </summary>
public static class CodeGraphGenerationServiceExtensions
{
    public static IServiceCollection AddCodeGraphGeneration(this IServiceCollection services)
    {
        services.AddSingleton<ITaskExecutor, CodeGraphTaskExecutor>();
        return services;
    }
} 