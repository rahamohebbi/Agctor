namespace AgctorSDK.Host.Models;

/// <summary>
/// Configures the embedding diagnostics panel on dashboard pages.
/// </summary>
public class EmbeddingDebugComponentModel
{
    public string ComponentId { get; set; } = "embedding-debug";
    public string Title { get; set; } = "Embedding vectors (debug)";
    public string Description { get; set; } = "Load stored vectors to inspect or visualize (table + 2D scatter using first two dimensions).";
}
