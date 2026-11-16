using System.Text.Json;
using AgctorSDK.Host.Services.Scenarios;
using AgctorSDK.Host.Services.VideoWorkflow;
using Xunit;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class ScenarioFlowGraphProjectorTests
{
    [Fact]
    public void Project_maps_scene_video_admission_flow_to_canonical_mailboxes()
    {
        var flow = SampleFlow();
        var projection = ScenarioFlowGraphProjector.Project(flow);
        Assert.NotNull(projection);
        var ids = projection!.Skeleton.Nodes.Select(n => n.Id).ToHashSet();
        Assert.Contains("Human", ids);
        Assert.Contains("Director", ids);
        Assert.Contains("Audio Engineer", ids);
        Assert.Contains(ids, id => id.Contains("Finished", StringComparison.OrdinalIgnoreCase) || id == "Output");
        Assert.Contains(projection.Skeleton.Edges, e => e.From == "Director" && e.To == "Audio Engineer");
        Assert.True(projection.Layouts.ContainsKey("Director"));
        Assert.Equal(0, projection.Layouts["Director"].X);
        Assert.Equal(180, projection.Layouts["Director"].Y);
    }

    [Fact]
    public void Compose_keeps_flow_skeleton_and_adds_observed_specialists()
    {
        var projection = ScenarioFlowGraphProjector.Project(SampleFlow())!;
        var observed = new AgctorSDK.Core.Workflows.AgentRunGraph(
            [new("Visual critic", "agent", null), new("Director", "agent", null)],
            [new("Director", "Visual critic", "Review", "completed", 1, DateTimeOffset.UtcNow)]);
        var composed = ScenarioFlowGraphProjector.Compose(projection, observed);
        Assert.Contains(composed.Nodes, n => n.Id == "Director");
        Assert.Contains(composed.Nodes, n => n.Id == "Audio Engineer");
        Assert.Contains(composed.Nodes, n => n.Id == "Visual critic");
        Assert.Contains(composed.Edges, e => e.From == "Director" && e.To == "Visual critic" && e.Count == 1);
    }

    private static ScenarioFlowDocument SampleFlow() => new()
    {
        SchemaVersion = "1.0",
        GraphId = "scene-video-chain",
        Nodes =
        [
            new() { Id = "input", Type = "ChatInput", Label = "Your idea" },
            new()
            {
                Id = "director",
                Type = "AgentLoop",
                Label = "Director",
                Config = JsonSerializer.SerializeToElement(new { workflow = "scene-video", maxAttempts = 3 })
            },
            new()
            {
                Id = "audio-engineer",
                Type = "AgentLoop",
                Label = "Audio Engineer",
                Config = JsonSerializer.SerializeToElement(new { workflow = "scene-video", role = "audio-engineer" })
            },
            new() { Id = "output", Type = "Output", Label = "Finished video with reviewed audio" }
        ],
        Edges =
        [
            new() { Id = "start", FromNodeId = "input", ToNodeId = "director", Mode = "sequential" },
            new() { Id = "finish", FromNodeId = "director", ToNodeId = "output", Mode = "sequential" },
            new() { Id = "audio-request", FromNodeId = "director", ToNodeId = "audio-engineer", Mode = "sequential" },
            new() { Id = "audio-report", FromNodeId = "audio-engineer", ToNodeId = "director", Mode = "sequential" }
        ],
        Ui = new ScenarioFlowUi
        {
            NodeLayouts = new Dictionary<string, ScenarioFlowNodeLayout>
            {
                ["input"] = new() { X = 0, Y = 0 },
                ["director"] = new() { X = 0, Y = 180 },
                ["audio-engineer"] = new() { X = 360, Y = 180 },
                ["output"] = new() { X = 0, Y = 360 }
            }
        }
    };
}
