using System.Net.Http.Json;
using AgctorSDK.Core.Agents;

namespace AgctorSDK.Host.Services;

/// <summary>Shared Ollama <c>/api/generate</c> call (non-streaming) for persona runs and flow router.</summary>
internal static class OllamaGenerateApi
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    internal static async Task<string> GenerateNonStreamingAsync(string prompt, CancellationToken cancellationToken)
    {
        var ollama = LLMAgent.GetConfiguredOllamaApiUrl().TrimEnd('/') + "/";
        var model = LLMAgent.GetConfiguredDefaultModel();
        var req = new { model, prompt, stream = false };
        var resp = await Http.PostAsJsonAsync(ollama + "api/generate", req, cancellationToken).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var doc = await resp.Content.ReadFromJsonAsync<OllamaGenDto>(cancellationToken: cancellationToken).ConfigureAwait(false);
        return doc?.response?.Trim() ?? "";
    }

    private sealed class OllamaGenDto
    {
        public string? response { get; set; }
    }
}
