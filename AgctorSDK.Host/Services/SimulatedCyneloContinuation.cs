// Compatibility snapshot of Cynelo.Application.Generation, verified 2026-11-03.
// Used only by the explicit H3 simulator; keep parity fixtures in sync when Cynelo changes.
namespace AgctorSDK.Host.Services;

/// <summary>
/// Prompts for last-frame continuations. The original story is context
/// (already happened), never a scene to generate again from the top.
/// </summary>
internal static class SimulatedCyneloContinuation
{
    /// <summary>
    /// Nudge the opening clip to finish on a still the next clip can match.
    /// </summary>
    public static string OpeningHold(string originalPrompt)
    {
        originalPrompt = originalPrompt.Trim();
        return
            $"""
            {originalPrompt}

            End on a still, holdable moment: the subject is fully in frame, motion is small, the camera is settled. Do not fade to black. Do not finish the story.
            """;
    }

    /// <summary>A saved scene is the next approved action, never historical context to skip.</summary>
    public static string ForScene(string prompt, int seconds) => $"""
        The supplied image is the first frame of this shot. Continue from its pose, identity, clothing, place and lighting.
        Keep the opening camera motion gentle. Follow the current shot below, including its exact dialogue; this action has not happened yet.

        Rest of this {seconds}-second shot:
        {prompt.Trim()}

        Preserve continuity with the opening frame while completing this shot's requested action and spoken lines.
        """;

    public static string Build(string originalPrompt, string continuationPrompt) =>
        ForChain(originalPrompt, segmentIndex: 2, segmentCount: 2, segmentSeconds: 15, extraPrompt: continuationPrompt);

    /// <summary>
    /// Auto-extend when the film is longer than one model clip.
    /// A user extend prompt becomes the next action; the first second always
    /// stays locked to the supplied frame.
    /// </summary>
    public static string ForChain(
        string originalPrompt,
        int segmentIndex,
        int segmentCount,
        int segmentSeconds,
        string? extraPrompt)
    {
        originalPrompt = originalPrompt.Trim();
        var nextBeat = extraPrompt?.Trim();
        if (string.IsNullOrWhiteSpace(nextBeat))
        {
            nextBeat =
                "Stay in this moment. Take one small next action that follows from the current pose. " +
                "Do not walk in again. Do not reintroduce anyone or re-establish the place.";
        }

        var part = segmentCount > 2
            ? $" This is shot {segmentIndex} of {segmentCount}. The story is already in progress."
            : "";

        return
            $"""
            The supplied image is the first frame of this shot. Animate from this exact pose. Do not start a new scene.{part}

            What already happened (do not replay it):
            {originalPrompt}

            Match this frame exactly: same person, face, clothes, place, lighting, time of day, camera, lens, framing, and color.

            First second: almost no camera move. Continue only the smallest motion already in the pose — breath, blink, weight shift, or a hand already moving. No cut. No snap zoom.

            Rest of this {segmentSeconds}-second shot:
            {nextBeat}

            Never: titles, jump cuts, new people, wardrobe changes, time skips, sudden lighting changes, or establishing the location again.
            """;
    }
}
