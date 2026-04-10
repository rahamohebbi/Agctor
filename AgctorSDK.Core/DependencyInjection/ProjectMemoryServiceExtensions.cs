using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Indexing;
using AgctorSDK.Core.ProjectMemory.Loading;
using AgctorSDK.Core.ProjectMemory.Parsing;
using AgctorSDK.Core.ProjectMemory.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgctorSDK.Core.DependencyInjection;

public static class ProjectMemoryServiceExtensions
{
    public static IServiceCollection AddAgctorProjectMemory(this IServiceCollection services)
    {
        services.AddSingleton<IProjectLoader, ProjectLoader>();
        services.TryAddSingleton<IProjectAgentSpecRegistry, ProjectAgentSpecRegistryFromLoader>();
        services.AddSingleton<IEntityRegistry, EntityRegistry>();
        services.AddSingleton<IDocumentParser, DocumentParser>();
        services.AddSingleton<IMemoryIntentProcessor, MemoryIntentProcessor>();
        services.AddSingleton<IDocumentProjectionService, DocumentProjectionService>();
        services.AddSingleton<ProjectMemory.Tools.ProjectMemoryOperations>();
        services.AddSingleton<SqliteRuntimeIndexStoreFactory>();
        services.AddSingleton<PostgresRuntimeIndexStoreFactory>();
        services.AddSingleton<IRuntimeIndexStoreFactory, SwitchingRuntimeIndexStoreFactory>();
        services.AddSingleton<RebuildCoordinator>();
        return services;
    }
}
