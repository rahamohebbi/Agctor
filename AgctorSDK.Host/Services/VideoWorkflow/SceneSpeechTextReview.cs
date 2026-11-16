using System.Text.RegularExpressions;

namespace AgctorSDK.Host.Services.VideoWorkflow;

public sealed record SceneSpeechTextReview(string Status, string Transcript, IReadOnlyList<SceneSpeechIssue> Issues,
    string ExpectedText, string Model, string Reviewer = "Audio Engineer")
{
    public static string Expected(SceneVideoClip clip)
    {
        var quoted = SceneNativeSpeech.Expected(clip);
        if (!string.IsNullOrWhiteSpace(quoted)) return quoted;
        // Screenplay narration is often parenthesized rather than quoted.
        return string.Join(" ", Regex.Matches(clip.Prompt, @"\((?:NARRATION|VOICEOVER|VOICE OVER)\s*:\s*(?<text>[^)]+)\)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Select(m => m.Groups["text"].Value.Trim()));
    }

    public static async Task<SceneSpeechTextReview> Evaluate(ISceneAudioEngineer audio, string context, string expected,
        string model, SceneSpeechReport speech, CancellationToken ct)
    {
        var review = await audio.ReviewSpeechAsync(context, model, speech, ct);
        var issues = review.Issues.ToList();
        if (!string.IsNullOrWhiteSpace(expected) && !SceneNativeSpeech.Matches(expected, speech.Transcript)) {
            foreach (var difference in SceneSpeechDifferences.Find(expected, speech.Transcript))
                if (!issues.Any(i => i.Start <= difference.Start && i.Start + i.Length >= difference.Start + difference.Length))
                    issues.Add(difference);
        }
        return new(issues.Count > 0 ? "concerns" : !review.Available ? "unavailable" : review.Understood ? "passed" : "uncertain",
            speech.Transcript, issues.OrderBy(i => i.Start).ToArray(), expected, model);
    }
}
