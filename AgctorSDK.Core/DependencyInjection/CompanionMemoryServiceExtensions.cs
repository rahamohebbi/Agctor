using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.ProjectMemory.Companion;
using AgctorSDK.Core.ProjectMemory.Orchestration;
using AgctorSDK.Core.ProjectMemory.Visual;
using AgctorSDK.Core.Sessions;
using Microsoft.Extensions.DependencyInjection;

namespace AgctorSDK.Core.DependencyInjection;

public static class CompanionMemoryServiceExtensions
{
    /// <summary>PRD-021 session-end ingest and proactive signals actor facades.</summary>
    public static IServiceCollection AddAgctorCompanionMemory(this IServiceCollection services)
    {
        services.AddSingleton<ActorBackedCompanionMemoryServices>(sp =>
            new ActorBackedCompanionMemoryServices(
                sp.GetRequiredService<IActorRuntimeAdapter>(),
                sp.GetRequiredService<ISessionStore>(),
                sp.GetRequiredService<IProjectMemoryPipelineRunner>(),
                sp.GetService<IVisualPipelineService>(),
                sp.GetService<VisualAssetCatalogStore>()));
        services.AddSingleton<ISessionEndIngestService>(sp => sp.GetRequiredService<ActorBackedCompanionMemoryServices>());
        services.AddSingleton<IProactiveSignalsService>(sp => sp.GetRequiredService<ActorBackedCompanionMemoryServices>());
        // PRD-022b privacy is registered in AddAgctorProjectMemory (IPrivacyMemoryService).
        return services;
    }
}
