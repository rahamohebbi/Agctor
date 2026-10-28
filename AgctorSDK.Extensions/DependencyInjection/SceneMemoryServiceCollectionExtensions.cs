using AgctorSDK.Core.SceneMemory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgctorSDK.Extensions.DependencyInjection;

/// <summary>Standalone registration for scene storage and retrieval, following the RAG registration pattern.</summary>
public static class SceneMemoryServiceCollectionExtensions
{
    /// <summary>
    /// Registers the default SQLite store and provider. Existing custom store/provider registrations are
    /// preserved, so applications can substitute persistence without changing controllers or agent tools.
    /// </summary>
    public static IServiceCollection AddAgctorSceneMemory(this IServiceCollection services, string databasePath)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (string.IsNullOrWhiteSpace(databasePath)) throw new ArgumentException("Database path is required.", nameof(databasePath));
        services.TryAddSingleton<ISceneMemoryStore>(_ => new SqliteSceneMemoryStore(databasePath));
        services.TryAddSingleton<ISceneMemoryProvider, SceneMemoryProvider>();
        return services;
    }
}
