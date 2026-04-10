using System;
using Microsoft.Extensions.DependencyInjection;

namespace AgctorSDK.Core.ProjectMemory;

/// <summary>
/// Lets PRD-013 project-memory agents resolve services despite actor runtime using <c>new Agent(id)</c> only.
/// Set once from the host after the root <see cref="IServiceProvider"/> is built.
/// </summary>
public static class ProjectMemoryServiceAccessor
{
    private static IServiceProvider? _provider;

    public static void Initialize(IServiceProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public static T GetRequiredService<T>() where T : notnull
    {
        if (_provider == null)
            throw new InvalidOperationException("ProjectMemoryServiceAccessor not initialized.");
        return _provider.GetRequiredService<T>();
    }
}
