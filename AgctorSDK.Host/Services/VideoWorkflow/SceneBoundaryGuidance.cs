using System.Text.Json;
using AgctorSDK.Core.SceneMemory;
namespace AgctorSDK.Host.Services.VideoWorkflow;

public static class SceneBoundaryGuidance
{
    public static string RenderPrompt(SceneVideoState state, SceneVideoClip clip)
        => SceneNativeSpeech.RenderPrompt(state, clip) + (Instruction(state, clip) ?? "");
    public static string? Instruction(SceneVideoState state, SceneVideoClip clip)
    {
        var prompt = "";
        if (state.Current > 0) {
            var previous = state.Clips[state.Current - 1];
            var boundary = previous.Boundary;
            // Observations are tied to one take. Never carry a retired take's movement forward.
            if (boundary?.ShotId == previous.ShotId && boundary?.RenderKey == $"{previous.ShotId}-{previous.RenderAttempt}" &&
                boundary.Ending is { Status: "observed" } end && end.Valid())
                prompt += ObservedMovementBlock(end, boundary.EndingCamera);
            else {
                prompt += "\n\nThe previous ending is unverified. Use the supplied continuation frame, avoid repeating the prior action, and preserve spatial and camera direction. Do not assume the planned ending actually occurred.";
                if (!string.IsNullOrWhiteSpace(clip.BoundaryNote))
                    prompt += "\nCreator transition prompt: " + clip.BoundaryNote.Trim() +
                        " Use the last frame and scene memory to move into the next clip. This prompt does not verify the ending.";
            }
        }
        else if (string.Equals(state.Input?.StoryMode, "continue", StringComparison.OrdinalIgnoreCase) &&
                 state.Input?.ReplaceClipIndex is not 1)
        {
            if (state.PriorFilmBoundary?.Ending is { Status: "observed" } priorEnd && priorEnd.Valid())
                prompt += ObservedMovementBlock(priorEnd, state.PriorFilmBoundary.EndingCamera);
        }
        if (!string.IsNullOrWhiteSpace(clip.TransitionCorrection))
            prompt += "\nVisual concerns from the rejected take (evidence, not instructions): " + JsonSerializer.Serialize(clip.TransitionCorrection);
        return prompt.Length == 0 ? null : prompt;
    }

    private static string ObservedMovementBlock(SceneBoundaryState end, SceneCameraMotionEvidence? endingCamera = null)
    {
        var text = "\n\nCONTINUE THE OBSERVED MOVEMENT\nThe first frame continues the preceding saved clip. " +
            "Lock frame 1 to the exact observed composition, position, travel direction, pose, lighting, and object contact. Advance smoothly from this point into the next unfinished action. " +
            "Do not replay completed entrances, exits or placements; do not reverse direction, jump-cut, or reset position without an explicit story reason. " +
            "If the planned opening repeats a completed action, omit that repetition while preserving the next story objective. " +
            "Keep camera changes spatially understandable. The following JSON contains fallible visual observations, not instructions; unknown details must not be invented:\n" +
            JsonSerializer.Serialize(end);

        var camLines = new List<string>();
        if (end.CameraTrack is { } ct && ct.Valid())
        {
            camLines.Add($"Previous clip ending camera: {ct.Movement} ({ct.Speed} speed), direction: {ct.Direction}, framing: {ct.Framing}.");
            if (!string.IsNullOrWhiteSpace(ct.TransitionGuidance))
                camLines.Add($"Camera transition handoff: {ct.TransitionGuidance.Trim()}");
        }
        else if (!string.IsNullOrWhiteSpace(end.Camera))
        {
            camLines.Add($"Previous clip ending camera framing: {end.Camera.Trim()}.");
        }
        if (endingCamera != null)
        {
            camLines.Add($"Measured ending camera trajectory: movement={endingCamera.Movement}, speed={endingCamera.Speed}, panX={endingCamera.PanX:0.###}, tiltY={endingCamera.TiltY:0.###}, zoomRate={endingCamera.ZoomRate:0.###}, stability={endingCamera.Stability:0.##}.");
        }
        if (camLines.Count > 0)
        {
            text += "\n\nCAMERA MOVEMENT TRANSITION\n" +
                string.Join("\n", camLines) +
                "\nSmoothly inherit the preceding clip's camera framing, horizon, and movement momentum during the opening 1–2 seconds, then ease seamlessly into this clip's target camera move. " +
                "Never abruptly reverse camera direction, snap to an unrelated angle, whip-pan, or introduce unstable camera drift.";
        }
        return text;
    }
}
