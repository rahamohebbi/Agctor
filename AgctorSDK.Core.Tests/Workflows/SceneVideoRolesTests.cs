using AgctorSDK.Core.Workflows;
using Xunit;

namespace AgctorSDK.Core.Tests.Workflows;

/// <summary>Phase B tool registration matrix: Audio Engineer owns audio tools; Director owns none.</summary>
public sealed class SceneVideoRolesTests
{
    [Fact]
    public void Tool_matrix_gives_audio_tools_only_to_audio_engineer()
    {
        Assert.Empty(SceneVideoRoles.DirectorTools);
        Assert.Equal(["Audio production", "Speech inspection"], SceneVideoRoles.AudioEngineerTools);
        Assert.False(SceneVideoRoles.MayInvoke(SceneVideoRoles.Director, "Audio production"));
        Assert.False(SceneVideoRoles.MayInvoke(SceneVideoRoles.Director, "Speech inspection"));
        Assert.False(SceneVideoRoles.MayInvoke(SceneVideoRoles.Director, "Video rendering"));
        Assert.True(SceneVideoRoles.MayInvoke(SceneVideoRoles.AudioEngineer, "Audio production"));
        Assert.True(SceneVideoRoles.MayInvoke(SceneVideoRoles.AudioEngineer, "Speech inspection"));
        Assert.False(SceneVideoRoles.MayInvoke(SceneVideoRoles.AudioEngineer, "Video rendering"));
        Assert.False(SceneVideoRoles.MayInvoke("Planner", "Audio production"));
    }

    [Theory]
    [InlineData("audio-plan")]
    [InlineData("audio-process")]
    [InlineData("audio-review")]
    [InlineData("clip-audio-plan")]
    [InlineData("clip-audio-process")]
    [InlineData("clip-audio-check")]
    [InlineData("narration-plan")]
    [InlineData("speech-intent")]
    [InlineData("speech-check")]
    public void Audio_engineer_owns_audio_work_phases(string phase) =>
        Assert.True(SceneVideoRoles.OwnsAudioWork(phase));

    [Theory]
    [InlineData("audio-decision")]
    [InlineData("clip-audio-decision")]
    public void Director_owns_audio_ask_phases(string phase)
    {
        Assert.True(SceneVideoRoles.OwnsDirectorAudioAsk(phase));
        Assert.False(SceneVideoRoles.OwnsAudioWork(phase));
    }
}
