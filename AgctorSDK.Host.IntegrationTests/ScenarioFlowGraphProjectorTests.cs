using System.Text.Json;
using AgctorSDK.Host.Services.Scenarios;
using AgctorSDK.Host.Services.VideoWorkflow;
using Xunit;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class ScenarioFlowGraphProjectorTests
{
    [Fact]
    public void Project_maps_expanded_scene_video_flow_to_specialist_mailboxes()
    {
        var flow = SampleExpandedFlow();
        var projection = ScenarioFlowGraphProjector.Project(flow);
        Assert.NotNull(projection);
        var ids = projection!.Skeleton.Nodes.Select(n => n.Id).ToHashSet();
        Assert.Contains("Human", ids);
        Assert.Contains("Director", ids);
        Assert.Contains("Planner", ids);
        Assert.Contains("Project agent", ids);
        Assert.Contains("Scene Memory agent", ids);
        Assert.Contains("Render agent", ids);
        Assert.Contains("Visual critic", ids);
        Assert.Contains("Editor agent", ids);
        Assert.Contains("Audio Engineer", ids);
        Assert.Contains("Video rendering", ids);
        Assert.Contains("Ollama", ids);
        Assert.Contains("Output", ids);
        Assert.Contains(projection.Skeleton.Edges, e => e.From == "Director" && e.To == "Render agent");
        Assert.Contains(projection.Skeleton.Edges, e => e.From == "Render agent" && e.To == "Video rendering");
        Assert.True(projection.Layouts.ContainsKey("Render agent"));
        Assert.Equal("Your idea", projection.Skeleton.Nodes.Single(n => n.Id == "Human").Detail);
        // Titles use flow labels — shared tool blurbs must not pile identical card names.
        Assert.Equal("Video rendering", projection.Skeleton.Nodes.Single(n => n.Id == "Video rendering").Detail);
        Assert.Equal("Audio production", projection.Skeleton.Nodes.Single(n => n.Id == "Audio production").Detail);
        Assert.Equal("output", projection.Skeleton.Nodes.Single(n => n.Id == "Output").Kind);
        Assert.Equal("Finished video with reviewed audio",
            projection.Skeleton.Nodes.Single(n => n.Id == "Output").Detail);
    }

    [Fact]
    public void DisplayDetail_prefers_label_over_shared_capability_blurbs()
    {
        var node = new ScenarioFlowNode
        {
            Id = "video-rendering",
            Type = "Capability",
            Label = "Video rendering",
            Config = JsonSerializer.SerializeToElement(new { kind = "tool", detail = "Cynelo API" })
        };
        Assert.Equal("Video rendering", ScenarioFlowGraphProjector.DisplayDetail(node));
        Assert.Equal("tool", ScenarioFlowGraphProjector.KindFor(node));
        Assert.Equal("output", ScenarioFlowGraphProjector.KindFor(new ScenarioFlowNode
        {
            Id = "output",
            Type = "Output",
            Label = "Finished video with reviewed audio"
        }));
    }

    [Fact]
    public void Compose_keeps_specialists_and_remaps_aliases()
    {
        var projection = ScenarioFlowGraphProjector.Project(SampleExpandedFlow())!;
        var observed = new AgctorSDK.Core.Workflows.AgentRunGraph(
            [
                new("Visual critic", "agent", null),
                new("Render agent", "agent", null),
                new("Video rendering", "tool", "Cynelo API"),
                new("Continuity critic", "agent", null)
            ],
            [
                new("Director", "Render agent", "Render", "started", 1, DateTimeOffset.UtcNow),
                new("Render agent", "Video rendering", "Clip", "started", 1, DateTimeOffset.UtcNow),
                new("Director", "Continuity critic", "Review", "completed", 1, DateTimeOffset.UtcNow)
            ],
            [
                new("ex-1", "Render agent", "Video rendering", "Rendering clip",
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1), "agent", "tool")
            ]);
        var composed = ScenarioFlowGraphProjector.Compose(projection, observed);
        Assert.Contains(composed.Nodes, n => n.Id == "Render agent");
        Assert.Contains(composed.Nodes, n => n.Id == "Video rendering");
        Assert.Contains(composed.Nodes, n => n.Id == "Visual critic");
        Assert.DoesNotContain(composed.Nodes, n => n.Id == "Continuity critic");
        Assert.Contains(composed.Edges, e => e.From == "Director" && e.To == "Render agent");
        Assert.Contains(composed.Edges, e => e.From == "Render agent" && e.To == "Video rendering");
        Assert.NotNull(composed.ActiveCalls);
        Assert.Contains(composed.ActiveCalls!, c => c.From == "Render agent" && c.To == "Video rendering");
    }

    [Fact]
    public void MapOntoFlow_keeps_specialists_when_present_on_flow()
    {
        var flowIds = SampleExpandedFlow().Nodes
            .Select(ScenarioFlowGraphProjector.CanonicalId)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal("Render agent", ScenarioFlowGraphProjector.MapOntoFlow("Render agent", flowIds));
        Assert.Equal("Video rendering", ScenarioFlowGraphProjector.MapOntoFlow("Video rendering", flowIds));
        Assert.Equal("Visual critic", ScenarioFlowGraphProjector.MapOntoFlow("Continuity critic", flowIds));
        Assert.Equal("Ollama", ScenarioFlowGraphProjector.MapOntoFlow("Ollama · qwen3:8b", flowIds));
        Assert.Equal("Audio production", ScenarioFlowGraphProjector.MapOntoFlow("Audio production", flowIds));
    }

    [Fact]
    public void SceneVideoScenario_accepts_expanded_specialist_flow()
    {
        Assert.Null(SceneVideoScenario.Validate(SampleExpandedFlow()));
        Assert.True(SampleExpandedFlow().Nodes.Count(SceneVideoScenario.IsDirector) == 1);
        Assert.True(SampleExpandedFlow().Nodes.Count(SceneVideoScenario.IsSpecialist) >= 6);
    }

    private static ScenarioFlowDocument SampleExpandedFlow()
    {
        ScenarioFlowNode Loop(string id, string label, string role, object? extra = null)
        {
            var cfg = extra == null
                ? JsonSerializer.SerializeToElement(new { workflow = "scene-video", role })
                : JsonSerializer.SerializeToElement(extra);
            return new() { Id = id, Type = "AgentLoop", Label = label, Config = cfg };
        }
        ScenarioFlowNode Cap(string id, string label, string kind, string detail) => new()
        {
            Id = id,
            Type = "Capability",
            Label = label,
            Config = JsonSerializer.SerializeToElement(new { kind, detail })
        };

        return new()
        {
            SchemaVersion = "1.0",
            GraphId = "scene-video-chain",
            Nodes =
            [
                new() { Id = "input", Type = "ChatInput", Label = "Your idea" },
                Loop("director", "Director", "director", new { workflow = "scene-video", role = "director", maxAttempts = 3 }),
                Loop("planner", "Planner", "planner"),
                Loop("project", "Project", "project"),
                Loop("memory", "Memory agent", "memory"),
                Loop("render", "Render", "render"),
                Loop("visual-critic", "Visual critic", "visual-critic"),
                Loop("editor", "Editor", "editor"),
                Loop("audio-engineer", "Audio Engineer", "audio-engineer"),
                Cap("ollama", "Ollama", "llm", "Planning / review model"),
                Cap("video-rendering", "Video rendering", "tool", "Cynelo API"),
                Cap("audio-production", "Audio production", "tool", "Cynelo API"),
                new() { Id = "output", Type = "Output", Label = "Finished video with reviewed audio" }
            ],
            Edges =
            [
                new() { Id = "start", FromNodeId = "input", ToNodeId = "director", Mode = "sequential" },
                new() { Id = "finish", FromNodeId = "director", ToNodeId = "output", Mode = "sequential" },
                new() { Id = "d-p", FromNodeId = "director", ToNodeId = "planner", Mode = "sequential" },
                new() { Id = "p-d", FromNodeId = "planner", ToNodeId = "director", Mode = "sequential" },
                new() { Id = "d-pr", FromNodeId = "director", ToNodeId = "project", Mode = "sequential" },
                new() { Id = "pr-d", FromNodeId = "project", ToNodeId = "director", Mode = "sequential" },
                new() { Id = "d-m", FromNodeId = "director", ToNodeId = "memory", Mode = "sequential" },
                new() { Id = "m-d", FromNodeId = "memory", ToNodeId = "director", Mode = "sequential" },
                new() { Id = "d-r", FromNodeId = "director", ToNodeId = "render", Mode = "sequential" },
                new() { Id = "r-d", FromNodeId = "render", ToNodeId = "director", Mode = "sequential" },
                new() { Id = "d-v", FromNodeId = "director", ToNodeId = "visual-critic", Mode = "sequential" },
                new() { Id = "v-d", FromNodeId = "visual-critic", ToNodeId = "director", Mode = "sequential" },
                new() { Id = "d-e", FromNodeId = "director", ToNodeId = "editor", Mode = "sequential" },
                new() { Id = "e-d", FromNodeId = "editor", ToNodeId = "director", Mode = "sequential" },
                new() { Id = "d-a", FromNodeId = "director", ToNodeId = "audio-engineer", Mode = "sequential" },
                new() { Id = "a-d", FromNodeId = "audio-engineer", ToNodeId = "director", Mode = "sequential" },
                new() { Id = "r-vr", FromNodeId = "render", ToNodeId = "video-rendering", Mode = "sequential" },
                new() { Id = "p-o", FromNodeId = "planner", ToNodeId = "ollama", Mode = "sequential" },
                new() { Id = "a-ap", FromNodeId = "audio-engineer", ToNodeId = "audio-production", Mode = "sequential" }
            ],
            Ui = new ScenarioFlowUi
            {
                NodeLayouts = new Dictionary<string, ScenarioFlowNodeLayout>
                {
                    ["input"] = new() { X = 0, Y = 320 },
                    ["director"] = new() { X = 0, Y = 160 },
                    ["render"] = new() { X = 280, Y = 520 },
                    ["video-rendering"] = new() { X = 560, Y = 500 },
                    ["output"] = new() { X = 0, Y = 520 }
                }
            }
        };
    }
}
