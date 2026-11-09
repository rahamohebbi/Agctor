using AgctorSDK.Core.SceneMemory;
using AgctorSDK.Host.Services;
using Moq;
using Xunit;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class SceneShotDurationTests
{
    public const string BakeryPrompt = "Create a 12-second cinematic video in a warm artisan bakery.\n\nA skilled baker opens a large oven and pulls out freshly baked sourdough loaves. Steam rises naturally. The scene transitions to a close-up of the golden loaves cooling on a wooden rack. Realistic, warm lighting, shallow depth of field, smooth cinematic camera motion, no text.";

    [Theory]
    [InlineData(BakeryPrompt, 12)]
    [InlineData("Make a 12 second video.", 12)]
    [InlineData("A 12-second clip.", 12)]
    [InlineData("A 12‑second cinematic video.", 12)]
    [InlineData("The clip should last 8 seconds.", 8)]
    [InlineData("Duration: 12s. Show the baker.", 12)]
    [InlineData("Create a 0.2-minute video.", 12)]
    [InlineData("Create a 2.5-second clip.", 2.5)]
    [InlineData("Create a 12-second video. Duration: 12s.", 12)]
    public void Reads_explicit_clip_lengths(string prompt, double seconds) => Assert.Equal(seconds, SceneShotDuration.Resolve(null, prompt));

    [Theory]
    [InlineData("The baker waits 12 seconds before opening the oven.")]
    [InlineData("The oven has a 12-second timer.")]
    [InlineData("Create a cinematic video with 12 loaves.")]
    [InlineData("A shot of the baker waiting 12 seconds.")]
    public void Does_not_turn_action_timings_or_counts_into_clip_length(string prompt) => Assert.Null(SceneShotDuration.Resolve(null, prompt));

    [Fact]
    public void Metadata_overrides_prompt_and_conflicting_instructions_require_clarification()
    {
        const string prompt = "Create a 12-second clip. Duration: 8 seconds.";
        Assert.Throws<ArgumentException>(() => SceneShotDuration.Resolve(null, prompt));
        Assert.Equal(9, SceneShotDuration.Resolve(9, prompt));
    }

    [Fact]
    public async Task Bakery_duration_survives_preview_apply_and_video_handoff()
    {
        var workspace = new SceneWorkspace("bakery", "Bakery", DateTimeOffset.UtcNow, [], [], []);
        var store = new Mock<ISceneMemoryStore>();
        store.Setup(s => s.GetAsync("bakery", It.IsAny<CancellationToken>())).ReturnsAsync(workspace);
        var interpreter = new Mock<IScenePromptInterpreter>();
        interpreter.Setup(i => i.InterpretAsync(workspace, BakeryPrompt, "test", It.IsAny<IProgress<ScenePromptProgress>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScenePromptExtraction([new("baker", "Baker", "Character")], [], [],
                [new("The baker removes loaves from the oven", ["baker"], BakeryPrompt)]));
        using var service = new ScenePromptService(store.Object, interpreter.Object);
        var review = await service.PreviewAsync("bakery", BakeryPrompt, null, "test", null, null, default);
        Assert.Equal(12, review.Preview.DurationSeconds);
        var applied = ScenePromptReducer.Apply(workspace, review.Preview);
        var shot = Assert.Single(applied.Prompts!);
        Assert.Equal(12, shot.DurationSeconds);
        var result = SceneVideoPromptBuilder.Prepare(applied, shot.Id, ScenePromptReducer.Revision(applied));
        Assert.Equal(12, result.DurationSeconds);
        Assert.Contains(BakeryPrompt, result.Prompt);
    }

    [Theory]
    [InlineData(null, 12)]
    [InlineData(8.0, 8)]
    public void Existing_shots_resolve_prompt_duration_without_mutating_history(double? stored, double expected)
    {
        var time = DateTimeOffset.UtcNow;
        var workspace = new SceneWorkspace("story", "Bakery", time, [], [],
            [new("shot", BakeryPrompt, "Bakery", "test", 1, 0, time, DurationSeconds: stored, DesignSnapshots: [])]);
        var revision = ScenePromptReducer.Revision(workspace);
        var result = SceneVideoPromptBuilder.Prepare(workspace, "shot", revision);
        Assert.Equal(expected, result.DurationSeconds);
        Assert.Equal(revision, ScenePromptReducer.Revision(workspace));
        Assert.Equal(stored, workspace.Prompts![0].DurationSeconds);
    }
}
