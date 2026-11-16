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

/// <summary>Phase A: topology catalog + graph skeleton ∪ observed.</summary>
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
        Assert.Contains(topo.Nodes, n => n.Id == "Video rendering" && n.Kind == "tool");
        Assert.DoesNotContain(topo.Nodes, n => n.Kind == "model");
        Assert.DoesNotContain(topo.Edges, e => e.From == "Director" && topo.Nodes.Any(n => n.Id == e.To && n.Kind is "tool" or "llm" or "memory"));
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/api/scenarios/people/topology")).StatusCode);
    }

    [Fact]
    public async Task Graph_endpoint_unions_skeleton_with_activity_and_aliases_visual_critic()
    {
        await using var factory = new Factory();
        using var http = factory.CreateClient();
        var store = factory.Services.GetRequiredService<IAgentRunStore>();
        var run = await store.CreateAsync(new AgentRun
        {
            ScenarioId = "scene-video-chain",
            Workflow = "scene-video",
            RequestId = Guid.NewGuid().ToString("N"),
            Summary = "Topology graph fixture",
            Status = "running",
            Phase = "critic",
            State = JsonSerializer.SerializeToElement(new { })
        });
        var lease = (await store.ClaimAsync(TimeSpan.FromMinutes(1)))!;
        await store.ActivityAsync(lease, "Director", "Routing to Visual critic",
            route: new("Director", "Continuity critic", "critic", Guid.NewGuid().ToString("N")));
        await store.ActivityAsync(lease, "Continuity critic", "Reviewed frames",
            route: new("Continuity critic", "Director", "critic", Guid.NewGuid().ToString("N")));

        using var response = await http.GetAsync($"/api/agent-runs/{run.Id}/graph");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("definition", out var definition));
        Assert.Equal("scene-video-chain", definition.GetProperty("graphId").GetString());
        var nodes = root.GetProperty("nodes").EnumerateArray().Select(n => n.GetProperty("id").GetString()!).ToHashSet();
        Assert.Contains("Director", nodes);
        Assert.Contains("Audio Engineer", nodes);
        Assert.Contains("Visual critic", nodes);
        Assert.DoesNotContain("Continuity critic", nodes);
        Assert.Contains("Video rendering", nodes);
        Assert.Contains("Scene Memory", nodes);
        var edges = root.GetProperty("edges").EnumerateArray().ToArray();
        Assert.Contains(edges, e => e.GetProperty("from").GetString() == "Director" && e.GetProperty("to").GetString() == "Visual critic");
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
                ["Agctor:SceneMemory:StorePath"] = Path.Combine(dir, "scene.db")
            }));
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<ISceneMemoryStore>(new SqliteSceneMemoryStore(Path.Combine(dir, "scene.db")));
                services.AddSingleton<IAgentRunStore>(new SqliteAgentRunStore(Path.Combine(dir, "runs.db")));
            });
        }
    }
}
