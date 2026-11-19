namespace AgctorSDK.Host.Services.VideoWorkflow;

/// <summary>
/// Word clocks are optional. Missing times must not force an endless Retry loop.
/// Cynelo may send timingStatus; Host normalizes so pace and skip-continue stay consistent.
/// </summary>
public static class SceneSpeechTiming
{
    public const string Ready = "ready";
    public const string Untimed = "untimed";
    public const string Unavailable = "unavailable";
    public const string Skipped = "skipped";

    /// <summary>Fill TimingStatus from Words when Cynelo omitted it. Never overwrite an explicit skip.</summary>
    public static SceneSpeechReport Normalize(SceneSpeechReport speech)
    {
        if (speech.TimingStatus == Skipped) return speech with { Words = null };
        if (speech.Words is { Count: > 0 }) return speech with { TimingStatus = Ready };
        if (speech.TimingStatus is Ready or Untimed or Unavailable) return speech with { Words = null };
        if (speech.Status is "detected" or "uncertain") return speech with { TimingStatus = Untimed, Words = null };
        if (speech.Status == Unavailable) return speech with { TimingStatus = Unavailable, Words = null };
        return speech;
    }

    public static bool OfferSkip(SceneSpeechReport? speech) =>
        speech?.TimingStatus is Untimed or Unavailable;

    /// <summary>
    /// Host speech-review prefers skip-timing; retry recovery prefers continue.
    /// continue-untimed is accepted as the same durable advance (CLRM-48 sheet).
    /// </summary>
    public static bool IsSkipChoice(string? choice) =>
        choice is "skip-timing" or "continue-untimed" or "continue";

    public static SceneSpeechReport MarkSkipped(SceneSpeechReport? speech) =>
        Normalize(speech ?? new(Unavailable, null, 0, "", "Word timing was skipped. Listen before deciding."))
            with { TimingStatus = Skipped, Words = null };
}
