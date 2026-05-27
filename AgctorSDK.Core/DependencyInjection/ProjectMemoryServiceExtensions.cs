using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Coref;
using AgctorSDK.Core.ProjectMemory.Indexing;
using AgctorSDK.Core.ProjectMemory.Loading;
using AgctorSDK.Core.ProjectMemory.Orchestration;
using AgctorSDK.Core.ProjectMemory.Inbox;
using AgctorSDK.Core.ProjectMemory.OutOfSchema;
using AgctorSDK.Core.ProjectMemory.Privacy;
using AgctorSDK.Core.ProjectMemory.Visual;
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
        services.AddSingleton<IGenericInboxStore, GenericInboxStore>();
        services.AddSingleton<IDocumentProjectionService, DocumentProjectionService>();
        // Default classifier is heuristic; the Host (or any host with an LLM client) can override
        // with LlmConfirmationIntentClassifier so natural consent phrasing is recognized without code edits.
        services.TryAddSingleton<IConfirmationIntentClassifier, HeuristicConfirmationIntentClassifier>();
        // Replay service back-fills entity files from confirmed.yaml using the freshest routing rules.
        services.AddSingleton<IGenericInboxReplayService, GenericInboxReplayService>();
        // Conversation coreference (PRD-019 Option B + F): persistent active-subject store + LLM-first resolver.
        // We always prefer the LLM resolver so consent/coref handling is multilingual and not heuristic-bound;
        // the heuristic fallback only kicks in when no IProjectMemoryLlmClient has been registered (rare).
        services.AddSingleton<IConversationFocusStore, ConversationFocusStore>();
        services.TryAddSingleton<IFocusSubjectResolver>(sp =>
        {
            var llm = sp.GetService<IProjectMemoryLlmClient>();
            if (llm == null)
                return new HeuristicFocusSubjectResolver();

            var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<FocusSubjectResolver>>();
            return new FocusSubjectResolver(llm, logger);
        });
        services.TryAddSingleton<IConversationCoreferenceResolver>(sp =>
        {
            var llm = sp.GetService<IProjectMemoryLlmClient>();
            if (llm == null)
                return new HeuristicConversationCoreferenceResolver();

            var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<LlmConversationCoreferenceResolver>>();
            return new LlmConversationCoreferenceResolver(llm, logger);
        });
        // Shared preprocessing helper so the pipeline runner and the playground SSE flow path
        // never diverge on pronoun handling (PRD-019 Option B + F).
        services.AddSingleton<IProjectMemoryCoreferenceCoordinator, ProjectMemoryCoreferenceCoordinator>();
        services.AddSingleton<ProjectMemory.Tools.ProjectMemoryOperations>();
        services.AddSingleton<SqliteRuntimeIndexStoreFactory>();
        services.AddSingleton<PostgresRuntimeIndexStoreFactory>();
        services.AddSingleton<IRuntimeIndexStoreFactory, SwitchingRuntimeIndexStoreFactory>();
        services.AddSingleton<RebuildCoordinator>();
        services.AddSingleton<IGenericInboxDecisionService, GenericInboxDecisionService>();
        services.AddSingleton<IPrivacyMemoryService>(sp =>
        {
            var purge = sp.GetService<IVisualPersonPrivacyPurge>();
            return new PrivacyMemoryService(purge);
        });
        return services;
    }
}
