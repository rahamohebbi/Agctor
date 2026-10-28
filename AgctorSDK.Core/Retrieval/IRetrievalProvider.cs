using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Rag;

namespace AgctorSDK.Core.Retrieval;

/// <summary>
/// Common read-only provider contract. Typed queries preserve each provider's retrieval semantics;
/// ingestion and state mutation belong to separate domain-specific contracts.
/// </summary>
public interface IRetrievalProvider<TRequest, TResult>
{
    /// <summary>Stable catalog identity.</summary>
    string ProviderId { get; }

    /// <summary>Readiness probe. Reuses the existing RAG health model for compatibility.</summary>
    Task<RagHealthResult> GetHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>Retrieves context without modifying provider content.</summary>
    Task<TResult> QueryAsync(TRequest request, CancellationToken cancellationToken = default);
}
