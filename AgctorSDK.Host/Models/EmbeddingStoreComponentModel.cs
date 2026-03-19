namespace AgctorSDK.Host.Models;

/// <summary>
/// Configures the embedding store panel on dashboard pages.
/// </summary>
public class EmbeddingStoreComponentModel
{
    public string ComponentId { get; set; } = "embedding-store";
    public string Title { get; set; } = "Embedding store";
    public string Description { get; set; } = "Code vectors stored for semantic search (e.g. by Indexer and Search agents).";
}
