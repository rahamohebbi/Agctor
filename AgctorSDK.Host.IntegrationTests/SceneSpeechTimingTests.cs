using AgctorSDK.Host.Services.VideoWorkflow;
using Xunit;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class SceneSpeechTimingTests
{
    [Fact]
    public void Words_mean_ready_and_missing_words_on_detected_speech_are_untimed()
    {
        var ready = SceneSpeechTiming.Normalize(new("detected", "en", .9, "Hello", "ok",
            Words: [new SceneSpeechWord("Hello", 0, 1)]));
        Assert.Equal(SceneSpeechTiming.Ready, ready.TimingStatus);
        Assert.Single(ready.Words!);

        var untimed = SceneSpeechTiming.Normalize(new("detected", "en", .9, "Hello", "ok"));
        Assert.Equal(SceneSpeechTiming.Untimed, untimed.TimingStatus);
        Assert.Null(untimed.Words);
        Assert.True(SceneSpeechTiming.OfferSkip(untimed));
    }

    [Fact]
    public void Skip_is_durable_and_clears_words()
    {
        var skipped = SceneSpeechTiming.MarkSkipped(new("detected", "en", .9, "Hello", "ok",
            Words: [new SceneSpeechWord("Hello", 0, 1)], TimingStatus: SceneSpeechTiming.Untimed));
        Assert.Equal(SceneSpeechTiming.Skipped, skipped.TimingStatus);
        Assert.Null(skipped.Words);
        Assert.False(SceneSpeechTiming.OfferSkip(skipped));
        Assert.Equal(SceneSpeechTiming.Skipped, SceneSpeechTiming.Normalize(skipped).TimingStatus);
    }

    [Theory]
    [InlineData("skip-timing")]
    [InlineData("continue-untimed")]
    [InlineData("continue")]
    public void Skip_choice_aliases_match_cynelo_sheet(string choice) =>
        Assert.True(SceneSpeechTiming.IsSkipChoice(choice));

    [Fact]
    public void Other_choices_are_not_timing_skips() =>
        Assert.False(SceneSpeechTiming.IsSkipChoice("retry"));

    [Theory]
    [InlineData(null)]
    [InlineData("unknown_code")]
    public void Unknown_production_conflicts_keep_the_safe_generic_copy(string? code) =>
        Assert.Contains("project needs attention", HttpSceneVideoProduction.ConflictMessage(code));

    [Fact]
    public void Known_audio_conflict_codes_get_specific_copy()
    {
        Assert.Contains("music", HttpSceneVideoProduction.ConflictMessage("music_unavailable"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ElevenLabs", HttpSceneVideoProduction.ConflictMessage("narration_cloud_unavailable"));
        Assert.Contains("different version", HttpSceneVideoProduction.ConflictMessage("audio_changed"));
    }

    [Theory]
    [InlineData("film_changed", "film changed")]
    [InlineData("wrong_clip", "target film slot")]
    [InlineData("clip_not_ready", "not ready")]
    [InlineData("project_conflict", "conflicts with the saved Cynelo project")]
    [InlineData("wrong_previous", "previous clip")]
    [InlineData("changed_prompt", "prompt changed")]
    public void Known_project_and_clip_conflict_codes_do_not_fall_back_to_generic_copy(string code, string expected)
    {
        var message = HttpSceneVideoProduction.ConflictMessage(code);
        Assert.Contains(expected, message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("project needs attention", message);
    }
}
