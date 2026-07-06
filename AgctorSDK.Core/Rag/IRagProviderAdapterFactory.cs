namespace AgctorSDK.Core.Rag;

/// <summary>
/// Creates <see cref="IRagProviderAdapter"/> instances by catalog id (mirrors <see cref="Interfaces.IActorRuntimeAdapterFactory"/>).
/// </summary>
public interface IRagProviderAdapterFactory
{
    /// <summary>All registered provider ids (includes None).</summary>
    IEnumerable<string> GetAvailableProviders();

    /// <summary>Resolve adapter for a catalog id.</summary>
    /// <exception cref="ArgumentException">Unknown provider id.</exception>
    IRagProviderAdapter CreateProvider(string providerId);

    /// <summary>Configured default from <see cref="RagOptions.DefaultProvider"/>.</summary>
    string GetDefaultProviderId();

    /// <summary>Adapter for the configured default provider.</summary>
    IRagProviderAdapter CreateDefaultProvider();

    /// <summary>True when id is registered in DI.</summary>
    bool IsProviderAvailable(string providerId);
}
