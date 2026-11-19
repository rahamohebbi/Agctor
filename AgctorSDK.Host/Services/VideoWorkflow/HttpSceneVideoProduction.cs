using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Host.Services.VideoWorkflow;

public sealed class HttpSceneVideoProduction(IHttpClientFactory clients, IOptionsMonitor<SceneVideoOptions> options) : ISceneVideoProduction
{
    public Task<SceneVideoProject> EnsureProjectAsync(string runId, string storyId, string mode, string name, CancellationToken ct,
        string? projectId = null, int? replaceClipIndex = null, string? continuityMedia = null, bool? keepLaterClips = null) =>
        Send<SceneVideoProject>(HttpMethod.Put, $"sequences/{Uri.EscapeDataString(runId)}/project",
            new { storyId, mode, name, projectId, replaceClipIndex, continuityMedia, keepLaterClips }, ct);
    public Task<SceneVideoEvidence> EvidenceAsync(string key, CancellationToken ct) =>
        Send<SceneVideoEvidence>(HttpMethod.Get, $"jobs/{Uri.EscapeDataString(key)}/evidence", null, ct);
    public Task<SceneVideoEvidence> EvidenceAsync(string key, bool recheck, CancellationToken ct) =>
        Send<SceneVideoEvidence>(HttpMethod.Get, $"jobs/{Uri.EscapeDataString(key)}/evidence" + (recheck ? "?shift=true" : ""), null, ct);
    public Task<SceneVideoDelivery> FinishAsync(string runId, string projectId, IReadOnlyList<string> keys, CancellationToken ct) =>
        Send<SceneVideoDelivery>(HttpMethod.Put, $"sequences/{Uri.EscapeDataString(runId)}/finish", new { projectId, keys }, ct);
    public Task<SceneAudioDraft> PrepareAudioAsync(string runId, string projectId, IReadOnlyList<string> keys, SceneAudioPlan plan, CancellationToken ct) =>
        Send<SceneAudioDraft>(HttpMethod.Put, $"sequences/{Uri.EscapeDataString(runId)}/audio", new { projectId, keys, continuousMusic = plan.ContinuousMusic, separator = plan.Separator, narration = plan.Narration, approved = plan.Approved, backgroundMusic = plan.BackgroundMusic, mix = plan.Mix, musicProfile = plan.MusicProfile }, ct);
    public Task<SceneAudioDraft> PrepareClipAudioAsync(string runId, string projectId, IReadOnlyList<string> keys, SceneAudioPlan plan, CancellationToken ct) =>
        Send<SceneAudioDraft>(HttpMethod.Put, $"sequences/{Uri.EscapeDataString(runId)}/audio", new { projectId, keys, continuousMusic = plan.ContinuousMusic, separator = plan.Separator, narration = plan.Narration, approved = plan.Approved, backgroundMusic = plan.BackgroundMusic, mix = plan.Mix, musicProfile = plan.MusicProfile, preview = true }, ct);
    public Task<SceneSpeechReport> InspectSpeechAsync(string key, CancellationToken ct) =>
        Send<SceneSpeechReport>(HttpMethod.Get, $"jobs/{Uri.EscapeDataString(key)}/speech", null, ct);
    public Task<SceneVideoDelivery> FinishAudioAsync(string runId, string projectId, IReadOnlyList<string> keys, string choice, CancellationToken ct) =>
        Send<SceneVideoDelivery>(HttpMethod.Put, $"sequences/{Uri.EscapeDataString(runId)}/finish", new { projectId, keys, audioChoice = choice }, ct);
    public async Task<bool> MatchingMusicAvailableAsync(CancellationToken ct)
    {
        try { return await Send<bool>(HttpMethod.Get, "matching-music-available", null, ct); }
        catch (HttpRequestException) { return false; }
    }
    public async Task<IReadOnlyList<string>> NarrationModelsAsync(CancellationToken ct)
    {
        try { return await Send<string[]>(HttpMethod.Get, "narration-models", null, ct); }
        catch (HttpRequestException) { return ["kokoro"]; } // Older studios still offer the local voice.
    }
    private async Task<T> Send<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var http = clients.CreateClient("SceneVideoProduction");
        using var request = new HttpRequestMessage(method, options.CurrentValue.RendererBaseUrl!.TrimEnd('/') + "/" + path);
        if (body != null) request.Content = JsonContent.Create(body);
        using var response = await http.SendAsync(request, ct);
        // Bodies can contain provider details. Map known codes; never publish raw upstream text.
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var code = await ReadConflictCode(response, ct);
            throw new SceneRenderInterventionException(ConflictMessage(code), code);
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct) ?? throw new InvalidOperationException("Missing studio result.");
    }

    private static async Task<string?> ReadConflictCode(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var detail = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            return detail.ValueKind == JsonValueKind.Object && detail.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String
                ? code.GetString() : null;
        }
        catch (JsonException) { return null; }
        catch (NotSupportedException) { return null; }
    }

    /// <summary>Same code vocabulary Cynelo SceneMemoryException uses on production routes.</summary>
    internal static string ConflictMessage(string? code) => code switch
    {
        "audio_changed" => "This audio draft belongs to a different version of the film. Review the clip in Cynelo, then retry or continue without word timing if clocks are stuck.",
        "audio_approval_changed" => "The approved clip audio no longer matches this take. Review its audio again in Cynelo.",
        "audio_script_changed" => "Approved narration changed. Review a revised clip before assembly.",
        "music_unavailable" => "Matching music is not configured. Preserve the original background, or set up the music service, then retry.",
        "narration_cloud_unavailable" => "Cloud narration is not configured. Choose the local voice, or set up ElevenLabs, then retry.",
        "film_not_empty" => "This is a new story. Open a new Cynelo project, or choose Continue a story to extend the existing film.",
        "story_film_mismatch" => "Open the Cynelo film for this story, then retry. Existing clips will be kept.",
        _ => "The story’s project needs attention. Check its saved clips in Cynelo, then retry. Existing projects are preserved."
    };
}
