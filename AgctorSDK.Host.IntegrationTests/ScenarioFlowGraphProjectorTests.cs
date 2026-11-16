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
        Assert.Contains("Output", ids);
        Assert.DoesNotContain(ids, id => id.Contains("Finished", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Your idea", projection.Skeleton.Nodes.Single(n => n.Id == "Human").Detail);
        Assert.Equal("Finished video with reviewed audio", projection.Skeleton.Nodes.Single(n => n.Id == "Output").Detail);
        Assert.Contains(projection.Skeleton.Edges, e => e.From == "Director" && e.To == "Audio Engineer");
        Assert.True(projection.Layouts.ContainsKey("Director"));
        Assert.True(projection.Layouts.ContainsKey("Output"));
        Assert.Equal(0, projection.Layouts["Director"].X);
        Assert.Equal(180, projection.Layouts["Director"].Y);
    }

    [Fact]
    public void Compose_keeps_flow_only_and_remaps_observed_specialists()
    {
        var projection = ScenarioFlowGraphProjector.Project(SampleFlow())!;
        var observed = new AgctorSDK.Core.Workflows.AgentRunGraph(
            [
                new("Visual critic", "agent", null),
                new("Director", "agent", null),
                new("Render agent", "agent", null),
                new("Video rendering", "tool", "Cynelo API")
            ],
            [
                new("Director", "Visual critic", "Review", "completed", 1, DateTimeOffset.UtcNow),
                new("Director", "Render agent", "Render", "started", 2, DateTimeOffset.UtcNow),
                new("Render agent", "Video rendering", "Clip", "started", 1, DateTimeOffset.UtcNow)
            ],
            [
                new("ex-1", "Render agent", "Video rendering", "Rendering clip",
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1), "agent", "tool")
            ]);
        var composed = ScenarioFlowGraphProjector.Compose(projection, observed);

        // Product picture = flow editor nodes only.
        Assert.Equal(4, composed.Nodes.Count);
        Assert.Contains(composed.Nodes, n => n.Id == "Director");
        Assert.Contains(composed.Nodes, n => n.Id == "Audio Engineer");
        Assert.Contains(composed.Nodes, n => n.Id == "Human");
        Assert.Contains(composed.Nodes, n => n.Id == "Output");
        Assert.DoesNotContain(composed.Nodes, n => n.Id == "Visual critic");
        Assert.DoesNotContain(composed.Nodes, n => n.Id == "Render agent");
        Assert.DoesNotContain(composed.Nodes, n => n.Id == "Video rendering");

        // No specialist edges invented; flow edges remain.
        Assert.DoesNotContain(composed.Edges, e => e.To == "Visual critic" || e.From == "Render agent");
        Assert.Contains(composed.Edges, e => e.From == "Director" && e.To == "Audio Engineer");

        // Pending work remaps onto Director so the flow node can animate.
        Assert.NotNull(composed.ActiveCalls);
        Assert.All(composed.ActiveCalls!, c =>
        {
            Assert.Equal("Director", c.From);
            Assert.Equal("Director", c.To);
        });
    }

    [Fact]
    public void MapOntoFlow_sends_audio_tools_to_audio_engineer()
    {
        var flowIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "Human", "Director", "Audio Engineer", "Output"
        };
        Assert.Equal("Audio Engineer", ScenarioFlowGraphProjector.MapOntoFlow("Audio production", flowIds));
        Assert.Equal("Director", ScenarioFlowGraphProjector.MapOntoFlow("ComfyUI", flowIds));
        Assert.Equal("Director", ScenarioFlowGraphProjector.MapOntoFlow("Ollama · qwen3:8b", flowIds));
        Assert.Equal("Output", ScenarioFlowGraphProjector.MapOntoFlow("Finished video with reviewed audio", flowIds));
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
                Label = "Director · Plan / Render / Review",
                Config = JsonSerializer.SerializeToElement(new { workflow = "scene-video", maxAttempts = 3 })
            },
            new()
            {
                Id = "audio-engineer",
                Type = "AgentLoop",
                Label = "Audio Engineer · Inspect / Narrate / Mix / Verify",
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
