using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.ProjectMemory.Resolution.Signals;

/// <summary>
/// Minimal embedding contract the resolution subsystem depends on. Real implementations (Ollama,
/// OpenAI, etc.) live outside Core; a no-op default keeps Core self-contained and embeddings
/// always-optional (PRD-018 §7: absence must not be a veto).
/// </summary>
public interface IEmbeddingProvider
{
    bool IsAvailable { get; }

    /// <summary>Return a fixed-length vector (dimension is provider-specific), or null when unavailable.</summary>
    Task<float[]?> EmbedAsync(string text, CancellationToken cancellationToken = default);
}

/// <summary>No-op fallback that always reports unavailable. Default when nothing is wired up.</summary>
public sealed class NullEmbeddingProvider : IEmbeddingProvider
{
    public bool IsAvailable => false;
    public Task<float[]?> EmbedAsync(string text, CancellationToken cancellationToken = default) => Task.FromResult<float[]?>(null);
}
