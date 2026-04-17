namespace AgctorSDK.Host.Services;

/// <summary>
/// Lists models installed in the local Ollama instance (<c>/api/tags</c>) for the configured base URL (PRD-015).
/// </summary>
public interface IOllamaModelCatalog
{
    /// <summary>Returns local models, or throws if Ollama is unreachable or returns an error.</summary>
    Task<IReadOnlyList<OllamaModelListItem>> ListLocalModelsAsync(CancellationToken cancellationToken = default);
}

/// <summary>Subset of Ollama tag fields needed for the dashboard.</summary>
public sealed class OllamaModelListItem
{
    public string Name { get; init; } = "";
    public long? Size { get; init; }
    public string? ModifiedAt { get; init; }
}
