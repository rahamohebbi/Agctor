using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Host.Services.VideoWorkflow;

/// <summary>Bridge contract: PUT jobs/{key} ensures one job; GET jobs/{key} reconciles it after a restart.</summary>
public sealed class HttpSceneVideoRenderer(IHttpClientFactory clients, IOptionsMonitor<SceneVideoOptions> options) : ISceneVideoRenderer
{
    public bool IsConfigured => Uri.TryCreate(options.CurrentValue.RendererBaseUrl, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https";
    public Task<SceneRenderJob> EnsureAsync(string key, SceneRenderRequest request, CancellationToken ct) =>
        SendAsync(HttpMethod.Put, key, request, ct);
    public Task<SceneRenderJob> GetAsync(string key, CancellationToken ct) => SendAsync(HttpMethod.Get, key, null, ct);
    private async Task<SceneRenderJob> SendAsync(HttpMethod method, string key, SceneRenderRequest? body, CancellationToken ct)
    {
        if (!IsConfigured) throw new InvalidOperationException("No video renderer is connected. Configure Agctor:SceneVideo:RendererBaseUrl.");
        var settings = options.CurrentValue;
        using var request = new HttpRequestMessage(method, settings.RendererBaseUrl!.TrimEnd('/') + "/jobs/" + Uri.EscapeDataString(key));
        if (body != null) request.Content = JsonContent.Create(body);
        request.Headers.Add("Idempotency-Key", key);
        using var http = clients.CreateClient("SceneVideoRenderer");
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable) {
            try {
                var detail = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(cancellationToken: ct);
                if (detail.ValueKind == System.Text.Json.JsonValueKind.Object && detail.TryGetProperty("code", out var code) && code.GetString() == "comfyui_unavailable")
                    throw new SceneRenderInterventionException("ComfyUI is not responding. Start ComfyUI or check its address in Cynelo Settings. Status checks will continue automatically. Check again only looks for the existing render; it does not submit another video.", "comfyui_unavailable");
            } catch (System.Text.Json.JsonException) { }
        }
        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            throw new SceneRenderInterventionException("Cynelo rejected the integration request. Check that its token-free Agctor rendering endpoints are available at the configured URL.");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            string? code = null;
            try
            {
                var detail = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(cancellationToken: ct);
                if (detail.ValueKind == System.Text.Json.JsonValueKind.Object && detail.TryGetProperty("code", out var value)
                    && value.ValueKind == System.Text.Json.JsonValueKind.String) code = value.GetString();
            }
            catch (System.Text.Json.JsonException) { /* Missing routes may return an empty or HTML response. */ }
            // Map known codes rather than publishing upstream bodies or treating configuration errors as transient outages.
            throw new SceneRenderInterventionException(code == "story_not_found"
                ? "Cynelo is reachable, but its rendering bridge cannot find this story in the configured studio. Set Cynelo's AgctorRendering:Owner to the owner of the selected story, then retry."
                : method == HttpMethod.Get
                    ? "Cynelo is reachable, but this render job was not found. Check that Agctor is connected to the same Cynelo server and studio used to submit it."
                    : "Cynelo is reachable, but the rendering route or requested resource was not found. Check Agctor's renderer URL and that Cynelo has the Agctor rendering integration enabled.");
        }
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var detail = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(cancellationToken: ct);
            var code = detail.TryGetProperty("code", out var value) ? value.GetString() : null;
            var message = code switch {
                "film_not_empty" => "This is a new story. Open a new Cynelo project, or choose Continue a story to extend the existing film.",
                "story_film_mismatch" => "Open the Cynelo film for this story, then retry. Its existing clips will be kept and the new clips added after them.",
                "film_changed" or "changed_prompt" or "wrong_previous" => "Cynelo's film or Scene Memory changed. Cancel this run and start a new sequence from the updated scene.",
                "renderer_busy" => "Cynelo is finishing another job. Wait for it to finish, then retry.",
                "submission_uncertain" or "interrupted_render" => "Cynelo lost track of this submission after an interruption. Inspect or recover its existing job before retrying; Agctor will keep the same request key.",
                "recovery_uncertain" => "The saved clip could not be matched to a render job. Check ComfyUI’s queue and history for the existing render, then check again. This only looks for that job; it does not start another video.",
                "recovery_cancelled" => "This render was cancelled in Cynelo. It will not be resumed automatically.",
                "recovery_not_found" => "The saved render is not available in the configured Cynelo studio. Check the studio connection before recovery.",
                "too_many_references" => "This shot has more than eight designs with reference images. Cynelo keeps at least one image per design. Reduce the linked designs, then start a new sequence.",
                _ => "Cynelo needs attention before this render can continue. Inspect the shot and rendering job in Cynelo, then retry." };
            throw new SceneRenderInterventionException(message);
        }
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<SceneRenderJob>(cancellationToken: ct)
            ?? throw new ArgumentException("The video renderer returned an empty job.");
        if (string.IsNullOrWhiteSpace(result.Id) || result.Status is not ("queued" or "running" or "completed" or "failed"))
            throw new ArgumentException("The renderer returned an unsupported job status.");
        if (result.Status == "completed" && !ValidArtifactUrl(result.VideoUrl))
            throw new ArgumentException("The renderer reported completion without a valid video URL.");
        if (result.LastFrameUrl != null && !ValidArtifactUrl(result.LastFrameUrl))
            throw new ArgumentException("The renderer returned an invalid last-frame URL.");
        return result;
    }
    public static bool ValidArtifactUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var url)
        && url.Scheme is "http" or "https";
}

public sealed class SceneRenderInterventionException(string message, string? code = null) : Exception(message) { public string? Code { get; } = code; }
