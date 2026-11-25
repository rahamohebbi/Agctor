using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Host.Services.VideoWorkflow;

public sealed class HttpSceneVideoProduction(IHttpClientFactory clients, IOptionsMonitor<SceneVideoOptions> options,
    ILogger<HttpSceneVideoProduction>? log = null) : ISceneVideoProduction
{
    public Task<SceneVideoProject> EnsureProjectAsync(string runId, string storyId, string mode, string name, CancellationToken ct,
        string? projectId = null, int? replaceClipIndex = null, string? continuityMedia = null, bool? keepLaterClips = null) =>
        Send<SceneVideoProject>(HttpMethod.Put, $"sequences/{Uri.EscapeDataString(runId)}/project",
            new { storyId, mode, name, projectId, replaceClipIndex, continuityMedia, keepLaterClips }, ct);
    public Task<SceneVideoEvidence> EvidenceAsync(string key, CancellationToken ct) =>
        Send<SceneVideoEvidence>(HttpMethod.Get, $"jobs/{Uri.EscapeDataString(key)}/evidence", null, ct);
    public Task<SceneVideoEvidence> EvidenceAsync(string key, bool recheck, CancellationToken ct) =>
        Send<SceneVideoEvidence>(HttpMethod.Get, $"jobs/{Uri.EscapeDataString(key)}/evidence" + (recheck ? "?shift=true" : ""), null, ct);
    public Task<SceneVideoDelivery> FinishAsync(string runId, string projectId, IReadOnlyList<string> keys, CancellationToken ct,
        int? replaceClipIndex = null, string? continuityMedia = null, bool? keepLaterClips = null) =>
        Send<SceneVideoDelivery>(HttpMethod.Put, $"sequences/{Uri.EscapeDataString(runId)}/finish",
            new { projectId, keys, replaceClipIndex, keepLaterClips, continuityMedia }, ct);
    public Task<SceneAudioDraft> PrepareAudioAsync(string runId, string projectId, IReadOnlyList<string> keys, SceneAudioPlan plan, CancellationToken ct,
        int? replaceClipIndex = null, string? continuityMedia = null, bool? keepLaterClips = null) =>
        Send<SceneAudioDraft>(HttpMethod.Put, $"sequences/{Uri.EscapeDataString(runId)}/audio",
            new { projectId, keys, continuousMusic = plan.ContinuousMusic, separator = plan.Separator, narration = plan.Narration, approved = plan.Approved, backgroundMusic = plan.BackgroundMusic, mix = plan.Mix, musicProfile = plan.MusicProfile, replaceClipIndex, keepLaterClips, continuityMedia }, ct);
    public Task<SceneAudioDraft> PrepareClipAudioAsync(string runId, string projectId, IReadOnlyList<string> keys, SceneAudioPlan plan, CancellationToken ct,
        int? replaceClipIndex = null, string? continuityMedia = null, bool? keepLaterClips = null) =>
        Send<SceneAudioDraft>(HttpMethod.Put, $"sequences/{Uri.EscapeDataString(runId)}/audio",
            new { projectId, keys, continuousMusic = plan.ContinuousMusic, separator = plan.Separator, narration = plan.Narration, approved = plan.Approved, backgroundMusic = plan.BackgroundMusic, mix = plan.Mix, musicProfile = plan.MusicProfile, preview = true, replaceClipIndex, keepLaterClips, continuityMedia }, ct);
    public Task<SceneSpeechReport> InspectSpeechAsync(string key, CancellationToken ct) =>
        Send<SceneSpeechReport>(HttpMethod.Get, $"jobs/{Uri.EscapeDataString(key)}/speech", null, ct);
    public Task<SceneVideoDelivery> FinishAudioAsync(string runId, string projectId, IReadOnlyList<string> keys, string choice, CancellationToken ct,
        int? replaceClipIndex = null, string? continuityMedia = null, bool? keepLaterClips = null) =>
        Send<SceneVideoDelivery>(HttpMethod.Put, $"sequences/{Uri.EscapeDataString(runId)}/finish",
            new { projectId, keys, audioChoice = choice, replaceClipIndex, keepLaterClips, continuityMedia }, ct);
    public async Task<bool> MatchingMusicAvailableAsync(CancellationToken ct)
    {
        try { return await Send<bool>(HttpMethod.Get, "matching-music-available", null, ct); }
        catch (HttpRequestException) { return false; }
    }
    public async Task<IReadOnlyList<string>> NarrationModelsAsync(CancellationToken ct)
    {
        try { return await Send<string[]>(HttpMethod.Get, "narration-models", null, ct); }
        // A missing or unreadable voice list must not abandon the revision. The local voice still works.
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException) { return ["kokoro"]; }
    }
    public async Task<SceneCueFrames?> CueFramesAsync(string key, CancellationToken ct)
    {
        try { return await Send<SceneCueFrames>(HttpMethod.Get, $"jobs/{Uri.EscapeDataString(key)}/cue-frames", null, ct); }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException) { return null; }
    }
    public async Task SyncTimelineAsync(string runId, SceneTimelineSyncRequest request, CancellationToken ct)
    {
        try { await Send<JsonElement>(HttpMethod.Put, $"sequences/{Uri.EscapeDataString(runId)}/timeline", request, ct); }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            log?.LogWarning(ex, "Multi-track timeline sync unavailable for run {RunId}", runId);
        }
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
            log?.LogWarning("Cynelo production conflict ({StatusCode}) on {Method} {Path}: code={ConflictCode}",
                (int)response.StatusCode, method, path, code ?? "(none)");
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
        "film_changed" => "The project’s film changed after this step started. Refresh the project in Cynelo, or start a new sequence from the updated film.",
        "wrong_clip" => "This step does not match the clip currently in the target film slot. Check the saved clips in Cynelo, then retry or keep the original audio.",
        "clip_not_ready" => "One of the saved clips is not ready for audio or assembly yet. Wait for it to finish saving in Cynelo, then retry.",
        "project_conflict" => "This request conflicts with the saved Cynelo project state. Check the project in Cynelo, then retry or start a new sequence.",
        "wrong_previous" => "The previous clip in Cynelo no longer matches this sequence’s continuity chain. Check the earlier clip in Cynelo, or start a new sequence.",
        "changed_prompt" => "The saved shot’s prompt changed outside this run. Cancel this run and start a new sequence from the updated scene.",
        _ => "The story’s project needs attention. Check its saved clips in Cynelo, then retry. Existing projects are preserved."
    };
}
