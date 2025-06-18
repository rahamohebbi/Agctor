using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace AgctorSDK.CodeGraph.Llm
{
    public sealed class OllamaLlmClient : ILlmClient
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;

        public OllamaLlmClient(HttpClient httpClient, string baseUrl = "http://localhost:11434")
        {
            _http = httpClient;
            _baseUrl = baseUrl.TrimEnd('/');
        }

        public async Task<string> CompleteAsync(string prompt, LlmOptions? options = null)
        {
            options ??= new LlmOptions();
            var request = new
            {
                model = options.Model,
                prompt,
                stream = false,
                options = new { temperature = options.Temperature, num_predict = options.MaxTokens }
            };
            var resp = await _http.PostAsJsonAsync($"{_baseUrl}/api/generate", request);
            resp.EnsureSuccessStatusCode();
            var payload = await resp.Content.ReadFromJsonAsync<OllamaResponse>();
            return payload?.response ?? string.Empty;
        }

        private record OllamaResponse(string response);
    }
} 