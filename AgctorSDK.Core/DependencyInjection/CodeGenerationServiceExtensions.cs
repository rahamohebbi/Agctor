using AgctorSDK.Core.Coding;
using AgctorSDK.Core.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace AgctorSDK.Core.DependencyInjection;

/// <summary>
/// DI helpers for code-generation components.
/// </summary>
public static class CodeGenerationServiceExtensions
{
    public static IServiceCollection AddSimpleCodeGeneration(this IServiceCollection services, string? outputRoot = null)
    {
        services.AddSingleton<ICodeGenerator>(_ => new SimpleCodeGenerator(outputRoot));
        services.AddSingleton<ITaskExecutor, CoderTaskExecutor>();
        return services;
    }
} 