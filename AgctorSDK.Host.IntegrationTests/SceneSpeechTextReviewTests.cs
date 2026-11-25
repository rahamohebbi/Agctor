using System.Text.Json;
using AgctorSDK.Core.Workflows;
using AgctorSDK.Host.Controllers;
using AgctorSDK.Host.Services.VideoWorkflow;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class SceneSpeechTextReviewTests
{
    private const string Expected = "Every evening, she climbs these stairs. Beyond the shore, someone is still finding their way home.";
    private const string Transcript = "Every evening she climbs these stairs. Beyond the shore someone is still finding their way home. A boy told the etchel up a button and that the roll bin has gone the way.";

    [Fact]
    public async Task Screenplay_narration_flags_the_extra_sentence_even_when_model_cannot_locate_it()
    {
        var clip = new SceneVideoClip { Prompt = "EXT. LIGHTHOUSE – DUSK. (NARRATION: " + Expected + ")" };
        Assert.Equal(Expected, SceneSpeechTextReview.Expected(clip));
        var audio = new Mock<ISceneAudioEngineer>();
        audio.Setup(a => a.ReviewSpeechAsync(It.IsAny<string>(), "test", It.IsAny<SceneSpeechReport>(), default))
            .ReturnsAsync(new SceneSpeechUnderstanding(false, []));
        var result = await SceneSpeechTextReview.Evaluate(audio.Object, clip.Prompt, Expected, "test", new("detected", "en", 1, Transcript, ""), default);
        Assert.Equal("concerns", result.Status);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("A boy told the etchel up a button and that the roll bin has gone the way", Transcript.Substring(issue.Start, issue.Length));
        Assert.Equal("These words were not in the requested dialogue.", issue.Reason);
        audio.VerifyAll();
    }

    [Theory]
    [InlineData(false, "unavailable")]
    [InlineData(true, "uncertain")]
    public async Task Unavailable_review_is_distinct_from_an_uncertain_opinion(bool available, string status)
    {
        var audio = new Mock<ISceneAudioEngineer>();
        audio.Setup(a => a.ReviewSpeechAsync(It.IsAny<string>(), "test", It.IsAny<SceneSpeechReport>(), default))
            .ReturnsAsync(new SceneSpeechUnderstanding(false, [], available));
        var result = await SceneSpeechTextReview.Evaluate(audio.Object, "", "", "test", new("detected", "en", 1, Transcript, ""), default);
        Assert.Equal(status, result.Status);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task Preview_review_uses_exact_transcript_and_selected_script_without_changing_run()
    {
        var run = new AgentRun { ScenarioId = "scene-video-chain", Revision = 7, Status = "waiting", State = JsonSerializer.SerializeToElement(new SceneVideoState {
            Model = "test", Clips = [new SceneVideoClip { Prompt = "(NARRATION: " + Expected + ")", NarrationText = "Welcome home." }]
        }) };
        var store = new Mock<IAgentRunStore>(MockBehavior.Strict);
        store.Setup(s => s.GetAsync(run.Id, default)).ReturnsAsync(run);
        var audio = new Mock<ISceneAudioEngineer>();
        audio.Setup(a => a.ReviewSpeechAsync(It.Is<string>(s => s.Contains("Welcome home.")), "test", It.Is<SceneSpeechReport>(s => s.Transcript == "Welcome home."), default))
            .ReturnsAsync(new SceneSpeechUnderstanding(true, []));
        var controller = new SceneSpeechReviewsController(store.Object, audio.Object);
        var response = Assert.IsType<OkObjectResult>(await controller.Review(run.Id, new("Welcome home.", 0, "improved"), default));
        var review = Assert.IsType<SceneSpeechTextReview>(response.Value);
        Assert.Equal("passed", review.Status);
        Assert.Equal("Welcome home.", review.Transcript);
        Assert.Equal("Welcome home.", review.ExpectedText);
        Assert.Equal(7, run.Revision); Assert.Equal("waiting", run.Status);
        Assert.IsType<BadRequestResult>(await controller.Review(run.Id, new(Transcript, 99), default));
        Assert.IsType<BadRequestResult>(await controller.Review(run.Id, new(""), default));
        audio.VerifyAll();
        store.Verify(s => s.GetAsync(run.Id, default), Times.Exactly(2));
        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Expected_reads_planned_dialogue_from_scene_memory_and_classifies_hallucination_vs_mismatch()
    {
        var clip = new SceneVideoClip { ShotId = "shot-1", Prompt = "Quiet moonlit ocean." };
        var workspace = new AgctorSDK.Core.SceneMemory.SceneWorkspace("ws-1", "Moon", DateTimeOffset.UtcNow, [], [], [
            new AgctorSDK.Core.SceneMemory.ScenePromptRecord(
                "shot-1", clip.Prompt, "Clip 1/1", "test", 1, 1, DateTimeOffset.UtcNow,
                TimedEvents: [
                    new AgctorSDK.Core.SceneMemory.SceneTimedEvent(
                        "ev-1", "dialogue", "Planned line", new(1000, 4000), [], "planned", null, "Stay close to the light.")
                ])
        ]);

        Assert.Equal("Stay close to the light.", SceneSpeechTextReview.Expected(clip, workspace));

        var audio = new Mock<ISceneAudioEngineer>();
        audio.Setup(a => a.ReviewSpeechAsync(It.IsAny<string>(), "test", It.IsAny<SceneSpeechReport>(), default))
            .ReturnsAsync(new SceneSpeechUnderstanding(false, [new(0, 47, "Unrelated dialogue")]));

        var hallucinated = await SceneSpeechTextReview.Evaluate(audio.Object, clip.Prompt, "", "test",
            new("detected", "en", .95, "Fits in a collage, sometimes no need to sin alone", ""), default);
        Assert.Equal("hallucinated-speech", hallucinated.Diagnosis);
        Assert.Equal("Dialogue Supervisor", hallucinated.Reviewer);

        var mismatched = await SceneSpeechTextReview.Evaluate(audio.Object, clip.Prompt, "Stay close to the light.", "test",
            new("detected", "en", .95, "Fits in a collage, sometimes no need to sin alone", ""), default);
        Assert.Equal("mismatched-dialogue", mismatched.Diagnosis);
    }
}
