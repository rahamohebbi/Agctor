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
public sealed class OllamaConfiguredCompletionClient(AgctorSDK.Core.Utils.ActivityTracking.IActivityTracker? tracker = null) : IProjectMemoryLlmClient
{
    // One client for all configured-default calls — avoids duplicate sockets and matches prior Host static clients.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    public async Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var model = ModelSelectionScope.GenerationModel;
        using var activity = tracker?.StartActivity("llm.completion");
        activity?.SetAttribute("model", model);
        activity?.SetAttribute("modelSource", ModelSelectionScope.Selection?.Source ?? "default");
        activity?.SetTimelineDetailJson(System.Text.Json.JsonSerializer.Serialize(new { model, source = ModelSelectionScope.Selection?.Source ?? "default", role = ModelSelectionScope.Selection?.Role ?? ModelRoles.Generation }));
        try
        {
            var result = await OllamaGenerateHttp.GenerateNonStreamingTextAsync(Http,
                OllamaRuntimeConfiguration.GetApiUrlWithTrailingSlash(), model, prompt, cancellationToken);
            activity?.SetStatus(AgctorSDK.Core.Utils.ActivityTracking.ActivityStatus.Ok);
            return result;
        }
        catch (Exception ex) { activity?.RecordException(ex); throw; }
    }
}
