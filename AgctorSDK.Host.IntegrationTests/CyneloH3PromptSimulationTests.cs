using System.Text.Json;
using AgctorSDK.Host.Models.SceneMemory;
using AgctorSDK.Host.Services;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class CyneloH3PromptSimulationTests
{
    // Golden results generated from Cynelo's actual ContinuationPrompt, ReferencePrompt,
    // and H3Timing sources on 2026-11-09, rather than from this simulation.
    public static IEnumerable<object[]> CyneloCases()
    {
        using var stream = typeof(CyneloH3PromptSimulationTests).Assembly.GetManifestResourceStream(
            "AgctorSDK.Host.IntegrationTests.Fixtures.cynelo-h3-prompt-parity.json")!;
        using var document = JsonDocument.Parse(stream);
        foreach (var item in document.RootElement.EnumerateArray()) yield return [item.GetRawText()];
    }
    private static SceneVideoPromptResponse Handoff(string prompt, double? duration = 12) =>
        new("workspace", "revision", "shot", "Test shot", duration, prompt, [], [], [], []);

    [Theory, MemberData(nameof(CyneloCases))]
    public void Matches_actual_Cynelo_formatter(string json)
    {
        using var document = JsonDocument.Parse(json);
        var c = document.RootElement;
        var input = new SimulateSceneVideoRequest("revision", c.GetProperty("existingClips").GetInt32(),
            c.GetProperty("durationSeconds").GetInt32(), c.GetProperty("referenceLabels").EnumerateArray().Select(l => l.GetString()!).ToArray());
        var result = CyneloH3PromptSimulation.Prepare(Handoff(c.GetProperty("prompt").GetString()!), input);
        Assert.Equal(c.GetProperty("modelPrompt").GetString(), result.ModelPrompt);
        Assert.Equal(c.GetProperty("continuationPrompt").GetString(), result.ContinuationPrompt);
        Assert.Equal(c.GetProperty("modelSeconds").GetInt32(), result.ModelSeconds);
    }

    [Fact]
    public void Uses_saved_duration_and_discloses_continuation_and_trimming()
    {
        var result = CyneloH3PromptSimulation.Prepare(Handoff("Raha runs."), new("revision", 1));
        Assert.Equal(12, result.TargetSeconds); Assert.Equal(15, result.ModelSeconds);
        Assert.Contains(result.Warnings, w => w.Contains("current instructions"));
        Assert.Contains(result.Warnings, w => w.Contains("trims"));
        Assert.DoesNotContain("What already happened", result.ModelPrompt);
    }

    [Theory]
    [InlineData(null)] [InlineData(0d)] [InlineData(16d)] [InlineData(2.5)]
    public void Missing_or_unsupported_duration_requires_explicit_choice(double? duration) =>
        Assert.Throws<ArgumentException>(() => CyneloH3PromptSimulation.Prepare(Handoff("Action", duration), new("revision")));

    [Fact]
    public void Duration_override_is_explicit_and_does_not_rewrite_source_prompt()
    {
        var result = CyneloH3PromptSimulation.Prepare(Handoff("A 20-second shot.", 20), new("revision", DurationSeconds: 10));
        Assert.Equal(10, result.TargetSeconds); Assert.Equal(20, result.Handoff.DurationSeconds);
        Assert.Contains("20-second", result.ModelPrompt);
        Assert.Contains(result.Warnings, w => w.Contains("does not rewrite"));
    }
}
