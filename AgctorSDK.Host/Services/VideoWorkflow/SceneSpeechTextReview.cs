using System.Text.RegularExpressions;
using AgctorSDK.Core.SceneMemory;

namespace AgctorSDK.Host.Services.VideoWorkflow;

public sealed record SceneSpeechTextReview(string Status, string Transcript, IReadOnlyList<SceneSpeechIssue> Issues,
    string ExpectedText, string Model, string Reviewer = "Dialogue Supervisor", string? Diagnosis = null)
{
    public static string Expected(SceneVideoClip clip) => Expected(clip, null);

    public static string Expected(SceneVideoClip clip, SceneWorkspace? workspace)
    {
        // Explicit human speech correction always supersedes earlier script/memory lines.
        if (SceneNativeSpeech.Lines(clip.SpeechInstruction ?? "").Count > 0)
            return SceneNativeSpeech.Expected(clip);

        // Prefer planned dialogue recorded in Scene Memory for this shot.
        var plannedDialogue = workspace?.Prompts?
            .FirstOrDefault(p => p.Id == clip.ShotId)?.TimedEvents?
            .Where(e => string.Equals(e.Kind, "dialogue", StringComparison.OrdinalIgnoreCase)
                && string.Equals(e.Status, "planned", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(e.Dialogue))
            .OrderBy(e => e.Timing.StartSeconds)
            .Select(e => e.Dialogue!.Trim())
            .ToArray();
        if (plannedDialogue is { Length: > 0 })
            return string.Join(" ", plannedDialogue);

        var quoted = SceneNativeSpeech.Expected(clip);
        if (!string.IsNullOrWhiteSpace(quoted)) return quoted;
        // Screenplay narration is often parenthesized rather than quoted.
        return string.Join(" ", Regex.Matches(clip.Prompt, @"\((?:NARRATION|VOICEOVER|VOICE OVER)\s*:\s*(?<text>[^)]+)\)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Select(m => m.Groups["text"].Value.Trim()));
    }

    public static string ClassifyDiagnosis(string expected, string expectedLanguage, SceneSpeechReport heard, SceneSpeechTextReview? review)
    {
        var hasTranscript = !string.IsNullOrWhiteSpace(heard.Transcript);
        var hasExpected = !string.IsNullOrWhiteSpace(expected);
        if (!hasTranscript)
            return expectedLanguage == "none" && heard.Status == "no-speech" ? "matched" : "uncertain";
        if (expectedLanguage == "none")
            return "hallucinated-speech";
        if (hasExpected)
        {
            if (!SceneNativeSpeech.Matches(expected, heard.Transcript))
                return "mismatched-dialogue";
            return review?.Status == "passed" ? "matched" : "mismatched-dialogue";
        }
        if (review is { Status: "concerns" })
            return "hallucinated-speech";
        return review?.Status == "passed" ? "matched" : "uncertain";
    }

    public static async Task<SceneSpeechTextReview> Evaluate(ISceneAudioEngineer audio, string context, string expected,
        string model, SceneSpeechReport speech, CancellationToken ct, string expectedLanguage = "auto")
    {
        var review = await audio.ReviewSpeechAsync(context, model, speech, ct);
        var issues = review.Issues.ToList();
        if (!string.IsNullOrWhiteSpace(expected) && !SceneNativeSpeech.Matches(expected, speech.Transcript)) {
            foreach (var difference in SceneSpeechDifferences.Find(expected, speech.Transcript))
                if (!issues.Any(i => i.Start <= difference.Start && i.Start + i.Length >= difference.Start + difference.Length))
                    issues.Add(difference);
        }
        var status = issues.Count > 0 ? "concerns" : !review.Available ? "unavailable" : review.Understood ? "passed" : "uncertain";
        var draft = new SceneSpeechTextReview(status, speech.Transcript, issues.OrderBy(i => i.Start).ToArray(), expected, model);
        var diagnosis = ClassifyDiagnosis(expected, expectedLanguage, speech, draft);
        return draft with { Diagnosis = diagnosis };
    }
}
