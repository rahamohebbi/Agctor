using AgctorSDK.Core.SceneMemory;
using Xunit;
using AgctorSDK.Host.Services.VideoWorkflow;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class SceneNarrationPaceTests
{
    private static readonly SceneSpeechWord[] SpokenWindow = [new("Hello", 4, 5.2), new("friend", 8, 9)];

    [Fact]
    public void Longer_original_window_slows_the_line_and_keeps_its_start()
    {
        var placed = SceneNarrationPace.Resolve(SpokenWindow, null, 2.5, null);
        Assert.Equal(0.5, placed.Speed);
        Assert.Equal(4, placed.StartSeconds);
        Assert.True(placed.Speed < 1);
        var dialogue = new SceneTimeRange(1500, 6500);
        placed = SceneNarrationPace.Resolve(SpokenWindow, dialogue, 2.5, null);
        Assert.Equal(0.5, placed.Speed);
        Assert.Equal(1.5, placed.StartSeconds);
        var plan = new SceneNarrationPlan("kokoro", "af_heart", "en", ["Hello friend"],
            [SceneNarrationPace.Window(SpokenWindow, dialogue, null)]);
        Assert.Equal(5, plan.Windows![0].TargetSeconds);
        Assert.Equal(1.5, plan.Windows[0].StartSeconds);
        Assert.Null(plan.Windows[0].PaceOverride);
        Assert.Null(plan.Softness);
    }

    [Fact]
    public void Missing_times_and_no_dialogue_keep_speed_one_and_the_default_start()
    {
        var placed = SceneNarrationPace.Resolve(null, null, 2.5, null);
        Assert.Equal(1, placed.Speed);
        Assert.Equal(SceneNarrationPace.DefaultStartSeconds, placed.StartSeconds);
        Assert.Null(placed.TargetSeconds);
        var plan = new SceneNarrationPlan("kokoro", "af_heart", "en", ["A journey begins."],
            [SceneNarrationPace.Window(null, null, null)]);
        Assert.Null(plan.Windows![0].TargetSeconds);
        Assert.Equal(0.2, plan.Windows[0].StartSeconds);
    }

    [Fact]
    public void A_line_that_only_fits_by_speeding_up_is_faster_than_the_window()
    {
        var placed = SceneNarrationPace.Resolve([new SceneSpeechWord("Hi", 1, 2)], null, 2.5, null);
        Assert.True(placed.Speed > 1);
        Assert.Equal(2.5, placed.Speed);
    }

    [Fact]
    public void Voice_settings_json_is_an_approval_and_other_text_is_a_script_revision()
    {
        Assert.True(SceneNarrationPace.TryVoiceSettings(null, out _, out _, out _));
        Assert.True(SceneNarrationPace.TryVoiceSettings("{\"softness\":0.4,\"accent\":\"warm\",\"paceOverride\":null}", out var softness, out var accent, out var pace));
        Assert.Equal(0.4, softness);
        Assert.Equal("warm", accent);
        Assert.Null(pace);
        Assert.False(SceneNarrationPace.TryVoiceSettings("make it slower", out _, out _, out _));
    }
}
