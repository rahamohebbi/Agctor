namespace AgctorSDK.Core.Rag;

/// <summary>
/// Optional adapter capability: list indexed collection/dataset ids (Cognee datasets, LightRAG corpora, …).
/// </summary>
public interface IRagCollectionCatalog
{
    /// <summary>Names of collections that already exist in the provider index.</summary>
    Task<IReadOnlySet<string>> ListCollectionIdsAsync(CancellationToken cancellationToken = default);
}
