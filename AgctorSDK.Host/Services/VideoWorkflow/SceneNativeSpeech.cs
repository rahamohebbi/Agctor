using System.Globalization;
using System.Text.RegularExpressions;

namespace AgctorSDK.Host.Services.VideoWorkflow;

/// <summary>Deterministic H3 dialogue conditioning. Never infers speech from arbitrary quoted signs or titles.</summary>
public static partial class SceneNativeSpeech
{
    // Existing H3 blocks are opaque: do not rewrite their spoken text or nested quotation marks.
    [GeneratedRegex(@"<d>\s*\[(?<language>[^\]\r\n]+)\]\s*(?<words>.*?)</d>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Dialogue();
    [GeneratedRegex("(?<cue>(?<!sign )(?<!label )(?<!banner )(?<!caption )(?<!subtitle )(?<!screen )\\b(?:says|say|asks|replies|whispers|shouts|narration|voiceover|dialogue|narrator)(?: in an? off-screen voiceover)?\\s*:?\\s*)[\"“](?<words>[^\"”\\r\\n]+)[\"”]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QuotedSpeech();
    [GeneratedRegex(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex Tokens();

    public static string LanguageName(string code)
    {
        if (code == "none") return "no speech";
        if (code is "auto" or "unknown") return "not specified";
        try { return CultureInfo.GetCultureInfo(code).EnglishName; }
        catch (CultureNotFoundException) { return code; }
    }
    public static string Format(string prompt, string language)
    {
        if (language is "none" or "auto" or "unknown") return prompt;
        var result = new System.Text.StringBuilder(); var start = 0;
        string Convert(string text) => QuotedSpeech().Replace(text, m =>
            m.Groups["cue"].Value + "<d>[" + LanguageName(language) + "] " + m.Groups["words"].Value + "</d>");
        foreach (Match block in Dialogue().Matches(prompt)) {
            result.Append(Convert(prompt[start..block.Index])).Append(block.Value);
            start = block.Index + block.Length;
        }
        return result.Append(Convert(prompt[start..])).ToString();
    }
    public static IReadOnlyList<string> Lines(string prompt)
    {
        var items = Dialogue().Matches(prompt).Cast<Match>().Select(m => (m.Index, Text:m.Groups["words"].Value)).ToList();
        // Mask tagged dialogue before looking for ordinary speech quotes, preserving offsets.
        var masked = Dialogue().Replace(prompt, m => new string(' ', m.Length));
        items.AddRange(QuotedSpeech().Matches(masked).Cast<Match>().Select(m => (m.Index, m.Groups["words"].Value)));
        return items.OrderBy(x=>x.Index).Select(x=>x.Text).ToArray();
    }
    private static string[] Words(string text) => Tokens().Matches(text.ToLowerInvariant().Replace("’", "").Replace("'", ""))
        .Select(m=>m.Value).ToArray();
    public static bool Matches(string expected, string heard) => Words(expected).SequenceEqual(Words(heard));
    public static string Expected(SceneVideoClip clip) => string.Join(" ", Lines(
        Lines(clip.SpeechInstruction ?? "").Count > 0 ? clip.SpeechInstruction! : clip.Prompt));

    public static string? PlanConcern(string original, string clarifications, IReadOnlyList<string> clips, IReadOnlyList<int> durations, string language)
    {
        if (language is not ("none" or "auto" or "unknown") && clips.Any(p => Dialogue().Matches(p).Any(m =>
            !string.Equals(m.Groups["language"].Value, LanguageName(language), StringComparison.OrdinalIgnoreCase))))
            return "The planned dialogue uses a different language from the requested speech. Clarify the intended language before rendering.";
        var approved = Lines(clarifications); if (approved.Count == 0) approved = Lines(original);
        var planned = clips.SelectMany(Lines).ToArray();
        if (approved.Count > 0 && !Matches(string.Join(" ", approved), string.Join(" ", planned)))
            return "The plan changed or omitted the requested spoken words. Confirm the exact dialogue in quotes, or describe the changes you want. No video has been submitted.";
        // A conservative English planning budget, not a model guarantee. Never shorten approved speech silently.
        if (language == "en") for(var i=0;i<clips.Count;i++) {
            var count=Words(string.Join(" ", Lines(clips[i]))).Length;
            if(count > Math.Max(1, (durations[i]-1)*2))
                return $"Clip {i+1} has {count} spoken words for {durations[i]} seconds. Provide shorter dialogue or start a longer sequence so H3 has time to finish clearly. No video has been submitted.";
        }
        return null;
    }
    public static string RenderPrompt(SceneVideoState state, SceneVideoClip clip)
    {
        var prompt = clip.VideoPrompt!;
        if (clip.NativeSpeechVersion == 0 || state.Input.NarrationMode == "dedicated" || state.ExpectedSpeechLanguage is "none" or "unknown" or "auto") return prompt;
        var source = clip.Prompt;
        var formatted = Format(source, state.ExpectedSpeechLanguage);
        // A creator's explicit replacement line supersedes the old dialogue, not its visuals.
        if (Lines(clip.SpeechInstruction ?? "").Count > 0) {
            formatted = Dialogue().Replace(formatted, "[speech replaced by the approved correction below]") +
                "\n" + Format(clip.SpeechInstruction!, state.ExpectedSpeechLanguage);
        }
        var index = prompt.IndexOf(source, StringComparison.Ordinal);
        return index >= 0 ? prompt[..index] + formatted + prompt[(index + source.Length)..] : prompt;
    }
    public const string Direction = "Speak only tagged dialogue, verbatim and in order, in the tagged language. Use clear diction, natural pacing and one consistent voice per speaker. Finish before the clip ends. Only the active speaker moves their lips. Keep music below speech.";
}
