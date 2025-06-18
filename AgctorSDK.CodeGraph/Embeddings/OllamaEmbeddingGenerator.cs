using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace AgctorSDK.CodeGraph.Embeddings
{
    /// <summary>
    /// Embedding generator that hits a local Ollama instance using the nomic-embed-text model (default).
    /// Endpoint: http://localhost:11434/api/generate (Ollama default REST port).
    /// Configurable via constructor parameters so callers can swap model or host.
    /// </summary>
    public sealed class OllamaEmbeddingGenerator : IEmbeddingGenerator
    {
        private readonly HttpClient _http;
        private readonly string _model;

        public OllamaEmbeddingGenerator(HttpClient httpClient, string model = "nomic-embed-text")
        {
            _http = httpClient;
            _model = model;
        }

        private record OllamaRequest(string model, string prompt);
        private record OllamaResponse(float[] embedding);

        public async Task<float[]> GenerateEmbeddingAsync(string text)
        {
            var request = new { model = _model, prompt = text, stream = false };
            var resp = await _http.PostAsJsonAsync("/api/embeddings", request);
            resp.EnsureSuccessStatusCode();
            var payload = await resp.Content.ReadFromJsonAsync<OllamaResponse>();
            return payload?.embedding ?? []; // C# 12 array literal
        }
    }
} 