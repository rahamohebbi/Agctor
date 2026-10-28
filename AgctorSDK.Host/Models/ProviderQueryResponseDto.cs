namespace AgctorSDK.Host.Models;

/// <summary>Common outcome fields for provider query APIs; derived responses retain domain-specific results.</summary>
public abstract class ProviderQueryResponseDto
{
    /// <summary>Whether the query completed, including valid queries with no matches.</summary>
    public bool Success { get; set; }
    /// <summary>Catalog identity of the queried provider.</summary>
    public string ProviderId { get; set; } = null!;
    /// <summary>Human-readable outcome or validation error.</summary>
    public string Message { get; set; } = null!;
}

/// <summary>Scene queries share the RAG outcome fields while preserving state, history and source records.</summary>
public sealed class SceneMemoryProviderQueryResponseDto : ProviderQueryResponseDto
{
    /// <summary>Typed scene retrieval result; absent when the query failed.</summary>
    public AgctorSDK.Core.SceneMemory.SceneQueryResult? Result { get; set; }
}
