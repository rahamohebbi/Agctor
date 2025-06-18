using System.Threading.Tasks;

namespace AgctorSDK.CodeGraph.Embeddings
{
    /// <summary>
    /// Provides vector embeddings for a given piece of text.
    /// Default implementation will call a local Ollama model (nomic-embed-text), but any backend can be swapped in.
    /// </summary>
    public interface IEmbeddingGenerator
    {
        /// <summary>
        /// Generates a floating-point embedding vector for <paramref name="text"/>.
        /// </summary>
        Task<float[]> GenerateEmbeddingAsync(string text);
    }
} 