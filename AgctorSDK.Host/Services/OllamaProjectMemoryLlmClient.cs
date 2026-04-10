using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.ProjectMemory.Orchestration;

namespace AgctorSDK.Host.Services;

/// <summary>Ollama <c>/api/generate</c> (non-streaming) for project-memory pipeline steps.</summary>
public sealed class OllamaProjectMemoryLlmClient : IProjectMemoryLlmClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    public async Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
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
