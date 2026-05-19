using AgctorSDK.Core.ProjectMemory.Companion;
using Microsoft.Extensions.DependencyInjection;

namespace AgctorSDK.Core.DependencyInjection;

public static class CompanionMemoryServiceExtensions
{
    /// <summary>PRD-021 session-end ingest and proactive signals actor facades.</summary>
    public static IServiceCollection AddAgctorCompanionMemory(this IServiceCollection services)
    {
        services.AddSingleton<ActorBackedCompanionMemoryServices>();
        services.AddSingleton<ISessionEndIngestService>(sp => sp.GetRequiredService<ActorBackedCompanionMemoryServices>());
        services.AddSingleton<IProactiveSignalsService>(sp => sp.GetRequiredService<ActorBackedCompanionMemoryServices>());
        // PRD-022b privacy is registered in AddAgctorProjectMemory (IPrivacyMemoryService).
        return services;
    }
}
