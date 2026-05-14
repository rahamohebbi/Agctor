using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Orchestration;

namespace AgctorSDK.Core.Ollama;

/// <summary>
/// Ollama completions using <see cref="OllamaRuntimeConfiguration"/> (Host singleton).
/// Shared by project-memory pipelines, persona runner, and scenario flow router.
/// </summary>
public sealed class OllamaConfiguredCompletionClient : IProjectMemoryLlmClient
{
    // One client for all configured-default calls — avoids duplicate sockets and matches prior Host static clients.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    public Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default) =>
        OllamaGenerateHttp.GenerateNonStreamingTextAsync(
            Http,
            OllamaRuntimeConfiguration.GetApiUrlWithTrailingSlash(),
            OllamaRuntimeConfiguration.GetDefaultModel(),
            prompt,
            cancellationToken);
}
