using AgctorSDK.Core.Workflows;
using Xunit;

namespace AgctorSDK.Core.Tests.Workflows;

/// <summary>Phase C tool registration matrix and mailbox ownership.</summary>
public sealed class SceneVideoRolesTests
{
    [Fact]
    public void Tool_matrix_is_exclusive_per_agent()
    {
        Assert.Empty(SceneVideoRoles.DirectorTools);
        Assert.Equal(["Audio production", "Speech inspection", "Ollama"], SceneVideoRoles.AudioEngineerTools);
        Assert.Equal(["Ollama", "Scene Memory"], SceneVideoRoles.PlannerTools);
        Assert.Equal(["Project storage"], SceneVideoRoles.ProjectTools);
        Assert.Equal(["Scene Memory", "Story storage"], SceneVideoRoles.MemoryTools);
        Assert.Equal(["Video rendering", "Frame sampler"], SceneVideoRoles.RenderTools);
        Assert.Equal(["Ollama", "Frame sampler"], SceneVideoRoles.VisualCriticTools);
        Assert.Equal(["Film assembly"], SceneVideoRoles.EditorTools);

        Assert.False(SceneVideoRoles.MayInvoke(SceneVideoRoles.Director, "Audio production"));
        Assert.False(SceneVideoRoles.MayInvoke(SceneVideoRoles.Director, "Video rendering"));
        Assert.False(SceneVideoRoles.MayInvoke(SceneVideoRoles.Director, "Ollama"));
        Assert.True(SceneVideoRoles.MayInvoke(SceneVideoRoles.AudioEngineer, "Audio production"));
        Assert.False(SceneVideoRoles.MayInvoke(SceneVideoRoles.AudioEngineer, "Video rendering"));
        Assert.True(SceneVideoRoles.MayInvoke(SceneVideoRoles.Planner, "Ollama · gemma4:latest"));
        Assert.False(SceneVideoRoles.MayInvoke(SceneVideoRoles.Planner, "Film assembly"));
        Assert.True(SceneVideoRoles.MayInvoke(SceneVideoRoles.Render, "Video rendering"));
        Assert.False(SceneVideoRoles.MayInvoke(SceneVideoRoles.Render, "Audio production"));
        Assert.True(SceneVideoRoles.MayInvoke(SceneVideoRoles.VisualCritic, "Frame sampler"));
        Assert.False(SceneVideoRoles.MayInvoke(SceneVideoRoles.VisualCritic, "Video rendering"));
        Assert.True(SceneVideoRoles.MayInvoke(SceneVideoRoles.Editor, "Film assembly"));
        Assert.False(SceneVideoRoles.MayInvoke(SceneVideoRoles.Project, "Story storage"));
        Assert.True(SceneVideoRoles.MayInvoke(SceneVideoRoles.Memory, "Story storage"));
    }

    [Theory]
    [InlineData("audio-plan", SceneVideoRoles.AudioEngineer)]
    [InlineData("audio-decision", SceneVideoRoles.Director)]
    [InlineData("plan", SceneVideoRoles.Planner)]
    [InlineData("project", SceneVideoRoles.Project)]
    [InlineData("prepare", SceneVideoRoles.Memory)]
    [InlineData("initialize", SceneVideoRoles.Memory)]
    [InlineData("submit", SceneVideoRoles.Render)]
    [InlineData("poll", SceneVideoRoles.Render)]
    [InlineData("critic", SceneVideoRoles.VisualCritic)]
    [InlineData("finish", SceneVideoRoles.Editor)]
    public void Owner_maps_phases_to_mailboxes(string phase, string expected)
    {
        var run = new AgentRun { Phase = phase, Status = "running" };
        Assert.Equal(expected, SceneVideoRoles.Owner(run));
    }

    [Fact]
    public void Owner_routes_tool_heavy_replies_to_specialists_not_director_hat_switch()
    {
        var continuity = new AgentRun
        {
            Phase = "review-wait", Status = "waiting",
            Question = new("q", "continuity", "Keep?", [new("accept", "Keep")]),
            Reply = new("q", "accept", null)
        };
        Assert.Equal(SceneVideoRoles.VisualCritic, SceneVideoRoles.Owner(continuity));

        var planning = new AgentRun
        {
            Phase = "plan-review", Status = "waiting",
            Question = new("q", "planning-retry", "Retry?", [new("retry", "Retry")]),
            Reply = new("q", "retry", null)
        };
        Assert.Equal(SceneVideoRoles.Planner, SceneVideoRoles.Owner(planning));

        var audio = new AgentRun
        {
            Phase = "audio-decision", Status = "waiting",
            Question = new("q", "audio-review", "Choose", [new("original", "Original")]),
            Reply = new("q", "original", null)
        };
        Assert.Equal(SceneVideoRoles.Director, SceneVideoRoles.Owner(audio));
    }

    [Theory]
    [InlineData("audio-plan")]
    [InlineData("narration-plan")]
    [InlineData("speech-check")]
    public void Audio_engineer_owns_audio_work_phases(string phase) =>
        Assert.True(SceneVideoRoles.OwnsAudioWork(phase));
}
