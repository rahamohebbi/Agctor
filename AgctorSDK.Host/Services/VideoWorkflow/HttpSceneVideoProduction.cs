using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Host.Services.VideoWorkflow;

public sealed class HttpSceneVideoProduction(IHttpClientFactory clients, IOptionsMonitor<SceneVideoOptions> options) : ISceneVideoProduction
{
    public Task<SceneVideoProject> EnsureProjectAsync(string runId, string storyId, string mode, string name, CancellationToken ct, string? projectId = null) =>
        Send<SceneVideoProject>(HttpMethod.Put, $"sequences/{Uri.EscapeDataString(runId)}/project", new { storyId, mode, name, projectId }, ct);
    public Task<SceneVideoEvidence> EvidenceAsync(string key, CancellationToken ct) =>
        Send<SceneVideoEvidence>(HttpMethod.Get, $"jobs/{Uri.EscapeDataString(key)}/evidence", null, ct);
    public Task<SceneVideoDelivery> FinishAsync(string runId, string projectId, IReadOnlyList<string> keys, CancellationToken ct) =>
        Send<SceneVideoDelivery>(HttpMethod.Put, $"sequences/{Uri.EscapeDataString(runId)}/finish", new { projectId, keys }, ct);
    public Task<SceneAudioDraft> PrepareAudioAsync(string runId, string projectId, IReadOnlyList<string> keys, SceneAudioPlan plan, CancellationToken ct) =>
        Send<SceneAudioDraft>(HttpMethod.Put, $"sequences/{Uri.EscapeDataString(runId)}/audio", new { projectId, keys, continuousMusic = plan.ContinuousMusic, separator = plan.Separator, narration = plan.Narration, approved = plan.Approved }, ct);
    public Task<SceneAudioDraft> PrepareClipAudioAsync(string runId, string projectId, IReadOnlyList<string> keys, SceneAudioPlan plan, CancellationToken ct) =>
        Send<SceneAudioDraft>(HttpMethod.Put, $"sequences/{Uri.EscapeDataString(runId)}/audio", new { projectId, keys, continuousMusic = plan.ContinuousMusic, separator = plan.Separator, narration = plan.Narration, approved = plan.Approved, preview = true }, ct);
    public Task<SceneSpeechReport> InspectSpeechAsync(string key, CancellationToken ct) =>
        Send<SceneSpeechReport>(HttpMethod.Get, $"jobs/{Uri.EscapeDataString(key)}/speech", null, ct);
    public Task<SceneVideoDelivery> FinishAudioAsync(string runId, string projectId, IReadOnlyList<string> keys, string choice, CancellationToken ct) =>
        Send<SceneVideoDelivery>(HttpMethod.Put, $"sequences/{Uri.EscapeDataString(runId)}/finish", new { projectId, keys, audioChoice = choice }, ct);
    private async Task<T> Send<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var http = clients.CreateClient("SceneVideoProduction");
        using var request = new HttpRequestMessage(method, options.CurrentValue.RendererBaseUrl!.TrimEnd('/') + "/" + path);
        if (body != null) request.Content = JsonContent.Create(body);
        using var response = await http.SendAsync(request, ct);
        // Bodies can contain provider details. Only expose a fixed, actionable message.
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            throw new SceneRenderInterventionException("The story’s project needs attention. Check its saved clips in Cynelo, then retry. Existing projects are preserved.");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct) ?? throw new InvalidOperationException("Missing studio result.");
    }
}
