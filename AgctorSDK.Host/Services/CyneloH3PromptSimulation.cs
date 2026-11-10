using AgctorSDK.Host.Models.SceneMemory;

namespace AgctorSDK.Host.Services;

/// <summary>Versioned compatibility simulation, not a recording of a submitted Cynelo job.</summary>
public static class CyneloH3PromptSimulation
{
    public const string Formatter = "cynelo-h3-2026-11-09";
    public static SceneVideoPromptSimulationResponse Prepare(SceneVideoPromptResponse handoff, SimulateSceneVideoRequest input)
    {
        var labels = input.ReferenceLabels ?? [];
        if (input.ExistingClips is < 0 or > 10000 || labels.Length > 8 || labels.Any(s => s == null || s.Length > 200))
            throw new ArgumentException("Use 0–10,000 existing film clips and up to eight reference labels of at most 200 characters each.");
        var duration = input.DurationSeconds ?? handoff.DurationSeconds;
        if (duration == null || !double.IsFinite(duration.Value) || duration < 1 || duration > 15 || duration != Math.Truncate(duration.Value))
            throw new ArgumentException("Choose a whole-second duration from 1 to 15 for this simulation. The saved shot is unchanged.");
        var target = (int)duration.Value;
        var modelSeconds = target <= 5 ? 5 : target <= 10 ? 10 : 15;
        var slot = input.ExistingClips + 1;
        var continuation = input.ExistingClips > 0;
        var staged = continuation
            ? SimulatedCyneloContinuation.ForScene(handoff.Prompt, modelSeconds)
            : handoff.Prompt.Trim();
        var modelPrompt = labels.Length > 0
            ? SimulatedCyneloReferences.Bind(staged, SimulatedCyneloReferences.TagsFor(labels, continuation ? "opening frame" : null))
            : staged;
        var warnings = new List<string> {
            "Simulation of the inspected Cynelo H3 formatter, not a captured model request. Match the active film and the actual uploaded image order in Cynelo; other versions or providers can behave differently.",
            "No model call, image upload, video generation, or story change was made."
        };
        if (continuation) warnings.Add("This shot's action and dialogue remain current instructions; the previous clip supplies only the opening-frame continuity.");
        if (labels.Length > 0) warnings.Add("Each label assumes its image is selected and successfully uploaded. AGCTOR cannot check Cynelo's image files. Labels are replaced with <Picture N> in the model prompt.");
        if (target != modelSeconds) warnings.Add($"The model generates {modelSeconds} seconds; Cynelo trims the result to {target} seconds.");
        if (input.DurationSeconds != null && input.DurationSeconds != handoff.DurationSeconds)
            warnings.Add("The duration is overridden for this simulation only; it does not rewrite duration wording in the saved prompt.");
        return new(handoff, Formatter, input.ExistingClips, slot, target, modelSeconds,
            labels.Length > 0 ? "Reference-to-video" : continuation ? "Last-frame continuation" : "Text-to-video",
            labels, staged, modelPrompt, warnings.ToArray());
    }
}
