using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Completion;

namespace AgctorSDK.Core.Completion
{
    /// <summary>
    /// Talks to a local Ollama <c>/api/generate</c> endpoint.
    /// Network and HTTP failures become <see cref="TextGenerationResult"/> values so the
    /// calling actor stays active and can use its fallback text.
    /// </summary>
    public sealed class OllamaTextGenerator : ITextGenerator
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiRoot;
        private readonly string _model;

        public OllamaTextGenerator(HttpClient httpClient, string apiRoot = "http://localhost:11434", string model = "mistral")
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _apiRoot = apiRoot.TrimEnd('/') + "/";
            _model = string.IsNullOrWhiteSpace(model) ? "mistral" : model;
        }

        public string Name => "ollama:" + _model;

        public async Task<TextGenerationResult> GenerateAsync(TextGenerationRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Prompt))
            {
                return new TextGenerationResult(false, string.Empty, Name, "Prompt is empty.");
            }

            var prompt = string.IsNullOrWhiteSpace(request.SystemPrompt)
                ? request.Prompt
                : request.SystemPrompt + "\n\n" + request.Prompt;

            try
            {
                using var response = await _httpClient.PostAsJsonAsync(
                    _apiRoot + "api/generate",
                    new OllamaGenerateBody(_model, prompt, false),
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    return new TextGenerationResult(false, string.Empty, Name, $"Ollama returned {(int)response.StatusCode}.");
                }

                var body = await response.Content.ReadFromJsonAsync<OllamaGenerateBodyResponse>(cancellationToken: cancellationToken);
                if (body == null || !body.Done || string.IsNullOrWhiteSpace(body.Response))
                {
                    return new TextGenerationResult(false, string.Empty, Name, "Ollama returned an empty response.");
                }

                return new TextGenerationResult(true, body.Response.Trim(), Name);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new TextGenerationResult(false, string.Empty, Name, ex.Message);
            }
        }

        private sealed record OllamaGenerateBody(
            [property: JsonPropertyName("model")] string Model,
            [property: JsonPropertyName("prompt")] string Prompt,
            [property: JsonPropertyName("stream")] bool Stream);

        private sealed class OllamaGenerateBodyResponse
        {
            [JsonPropertyName("response")]
            public string? Response { get; set; }

            [JsonPropertyName("done")]
            public bool Done { get; set; }
        }
    }
}
