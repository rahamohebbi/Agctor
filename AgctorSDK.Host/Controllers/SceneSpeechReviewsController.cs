using AgctorSDK.Core.Workflows;
using AgctorSDK.Host.Services.VideoWorkflow;
using Microsoft.AspNetCore.Mvc;

namespace AgctorSDK.Host.Controllers;

/// <summary>Read-only specialist review of the exact preview transcript. Never approves or rerenders a run.</summary>
[ApiController]
public sealed class SceneSpeechReviewsController(IAgentRunStore runs, ISceneAudioEngineer audio) : ControllerBase
{
    public sealed record ReviewRequest(string Transcript, int? Clip = null, string? AudioVersion = null);

    [HttpPost("api/agent-runs/{id}/speech-review")]
    public async Task<IActionResult> Review(string id, ReviewRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Transcript) || request.Transcript.Length > 20000 ||
            request.AudioVersion is not (null or "original" or "improved")) return BadRequest();
        var run = await runs.GetAsync(id, ct);
        if (run == null || run.ScenarioId != "scene-video-chain") return NotFound();
        var state = SceneVideoLoop.State(run);
        var expected = "";
        var context = "Full-film transcript: the complete script is unavailable. Check linguistic coherence only; earlier story dialogue may be present.";
        if (request.Clip is { } index) {
            if (index < 0 || index >= state.Clips.Count) return BadRequest();
            var clip = state.Clips[index];
            expected = request.AudioVersion == "improved" && !string.IsNullOrWhiteSpace(clip.NarrationText)
                ? clip.NarrationText : SceneSpeechTextReview.Expected(clip);
            context = System.Text.Json.JsonSerializer.Serialize(new { scene = clip.Prompt, correction = clip.SpeechInstruction, expectedSpeech = expected });
        }
        var result = await SceneSpeechTextReview.Evaluate(audio, context, expected, state.Model,
            new("detected", null, 0, request.Transcript, "Preview transcript; text review only."), ct);
        return Ok(result);
    }
}
