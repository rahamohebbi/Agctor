using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgctorSDK.Core.SceneMemory;
using AgctorSDK.Core.Workflows;
using AgctorSDK.Host.Services.VideoWorkflow;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgctorSDK.Host.IntegrationTests;

/// <summary>Topology catalog + graph skeleton ∪ observed + Phase D edit API.</summary>
public sealed class SceneVideoTopologyApiTests
{
    [Fact]
    public async Task Topology_endpoint_returns_validated_scene_video_document()
    {
        await using var factory = new Factory();
        using var http = factory.CreateClient();
        using var response = await http.GetAsync("/api/scenarios/scene-video-chain/topology");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var topo = (await response.Content.ReadFromJsonAsync<SceneVideoTopologyDocument>())!;
        Assert.Empty(SceneVideoTopologyValidator.Validate(topo));
        Assert.Equal("scene-video-chain", topo.GraphId);
        Assert.Equal("observed-only", topo.ModelEvidencePolicy);
        Assert.Contains(topo.Nodes, n => n.Id == "Director" && n.Kind == "agent");
        Assert.Contains(topo.Nodes, n => n.Id == "Visual critic" && n.Aliases != null && n.Aliases.Contains("Continuity critic"));
        Assert.Contains(topo.Nodes, n => n.Id == "Audio Engineer");
        Assert.Contains(topo.Nodes, n => n.Id == "Dialogue Supervisor" && n.Kind == "agent");
        Assert.Contains(topo.Nodes, n => n.Id == "Voice Actor" && n.Kind == "agent");
        Assert.Contains(topo.Nodes, n => n.Id == "Voice synthesis" && n.Kind == "tool");
        Assert.Contains(topo.Nodes, n => n.Id == "Video rendering" && n.Kind == "tool");
        Assert.DoesNotContain(topo.Nodes, n => n.Kind == "model");
        Assert.DoesNotContain(topo.Edges, e => e.From == "Director" && topo.Nodes.Any(n => n.Id == e.To && n.Kind is "tool" or "llm" or "memory"));
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/api/scenarios/people/topology")).StatusCode);
    }

    [Fact]
    public async Task Topology_put_saves_audio_and_layouts_and_rejects_unknown_layout_nodes()
    {
        await using var factory = new Factory();
        using var http = factory.CreateClient();

        using var bad = await http.PutAsJsonAsync("/api/scenarios/scene-video-chain/topology", new SceneVideoTopologyEditRequest
        {
            AudioEnabled = true,
            NodeLayouts = new Dictionary<string, SceneVideoTopologyLayout>
            {
                ["Director"] = new() { X = 40, Y = 50 },
                ["NotANode"] = new() { X = 1, Y = 2 }
            }
        });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        using var ok = await http.PutAsJsonAsync("/api/scenarios/scene-video-chain/topology", new SceneVideoTopologyEditRequest
        {
            AudioEnabled = false,
            NodeLayouts = new Dictionary<string, SceneVideoTopologyLayout>
            {
                ["Director"] = new() { X = 120, Y = 90 },
                ["Planner"] = new() { X = 280, Y = 140 }
            }
        });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var saved = (await ok.Content.ReadFromJsonAsync<SceneVideoTopologyDocument>())!;
        Assert.False(saved.AudioEnabled);
        Assert.NotNull(saved.Ui?.NodeLayouts);
        Assert.Equal(120, saved.Ui!.NodeLayouts!["Director"].X);
        Assert.Equal(90, saved.Ui.NodeLayouts["Director"].Y);

        using var get = await http.GetAsync("/api/scenarios/scene-video-chain/topology");
        var again = (await get.Content.ReadFromJsonAsync<SceneVideoTopologyDocument>())!;
        Assert.False(again.AudioEnabled);
        Assert.Equal(280, again.Ui!.NodeLayouts!["Planner"].X);

        Assert.Equal(HttpStatusCode.NotFound,
            (await http.PutAsJsonAsync("/api/scenarios/people/topology", new SceneVideoTopologyEditRequest { AudioEnabled = true })).StatusCode);
    }

    [Fact]
    public async Task Graph_endpoint_returns_flow_only_skeleton_without_observed_specialists()
    {
        await using var factory = new Factory();
        using var http = factory.CreateClient();
        var store = factory.Services.GetRequiredService<IAgentRunStore>();
        var run = await store.CreateAsync(new AgentRun
        {
            ScenarioId = "scene-video-chain",
            Workflow = "scene-video",
            RequestId = Guid.NewGuid().ToString("N"),
            Summary = "Flow graph fixture",
            Status = "running",
            Phase = "critic",
            State = JsonSerializer.SerializeToElement(new { })
        });
        var lease = (await store.ClaimAsync(TimeSpan.FromMinutes(1)))!;
        await store.ActivityAsync(lease, "Director", "Routing to Visual critic",
            route: new("Director", "Visual critic", "critic", Guid.NewGuid().ToString("N")));
        await store.ActivityAsync(lease, "Visual critic", "Reviewed frames",
            route: new("Visual critic", "Director", "critic", Guid.NewGuid().ToString("N")));

        using var response = await http.GetAsync($"/api/agent-runs/{run.Id}/graph");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        // Single definition surface: conversation flow (not topology catalog).
        Assert.True(root.TryGetProperty("flow", out var flow));
        Assert.Equal("scene-video-chain", flow.GetProperty("graphId").GetString());
        Assert.True(root.TryGetProperty("layouts", out var layouts));
        Assert.True(layouts.TryGetProperty("Director", out _));
        Assert.True(layouts.TryGetProperty("Output", out _));
        var nodes = root.GetProperty("nodes").EnumerateArray().Select(n => n.GetProperty("id").GetString()!).ToHashSet();
        Assert.Contains("Human", nodes);
        Assert.Contains("Director", nodes);
        Assert.Contains("Audio Engineer", nodes);
        Assert.Contains("Dialogue Supervisor", nodes);
        Assert.Contains("Voice Actor", nodes);
        Assert.Contains("Voice synthesis", nodes);
        Assert.Contains("Output", nodes);
        // Expanded default flow includes specialists + capabilities as the single picture.
        Assert.Contains("Render agent", nodes);
        Assert.Contains("Visual critic", nodes);
        Assert.Contains("Video rendering", nodes);
        var edges = root.GetProperty("edges").EnumerateArray().ToArray();
        Assert.Contains(edges, e => e.GetProperty("from").GetString() == "Director" && e.GetProperty("to").GetString() == "Audio Engineer");
        Assert.Contains(edges, e => e.GetProperty("from").GetString() == "Director" && e.GetProperty("to").GetString() == "Visual critic");
        Assert.Contains(edges, e => e.GetProperty("from").GetString() == "Render agent" && e.GetProperty("to").GetString() == "Video rendering");
    }

    private sealed class Factory : AgctorWebApplicationFactory
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "agctor-topo-" + Guid.NewGuid().ToString("N"));
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            Directory.CreateDirectory(dir);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agctor:SceneVideo:WorkerEnabled"] = "false",
                ["Agctor:SceneVideo:StorePath"] = Path.Combine(dir, "runs.db"),
                ["Agctor:SceneMemory:StorePath"] = Path.Combine(dir, "scene.db"),
                // Keep Phase D overlay out of the repo Config folder during tests.
                ["Agctor:SceneVideo:Topology:UserFile"] = Path.Combine(dir, "scene-video-topology.user.json")
            }));
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<ISceneMemoryStore>(new SqliteSceneMemoryStore(Path.Combine(dir, "scene.db")));
                services.AddSingleton<IAgentRunStore>(new SqliteAgentRunStore(Path.Combine(dir, "runs.db")));
            });
        }
    }
}
