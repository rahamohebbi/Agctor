using AgctorSDK.Core.Rag;

namespace AgctorSDK.Host.Services;

/// <summary>Persists RAG provider selection to appsettings.User.json (PRD-025 Tier A).</summary>
public interface IUserRagSettingsService
{
    Task PersistAsync(RagSettingsUpdate update, CancellationToken cancellationToken = default);
}

/// <summary>Values written under Agctor:Rag in appsettings.User.json.</summary>
public sealed class RagSettingsUpdate
{
    public string DefaultProvider { get; set; } = RagProviderIds.None;
    public LightRagProviderOptions? LightRAG { get; set; }
    public GraphitiProviderOptions? Graphiti { get; set; }
    public CogneeProviderOptions? Cognee { get; set; }
}
