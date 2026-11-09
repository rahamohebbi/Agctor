using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgctorSDK.Core.SceneMemory;
using AgctorSDK.Core.Workflows;
using AgctorSDK.Host.Services;
using AgctorSDK.Host.Services.VideoWorkflow;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class SceneVideoRunApiTests
{
    [Fact]
    public async Task Start_review_answer_complete_and_reconnect_over_http()
    {
        await using var factory = new Factory(); using var http = factory.CreateClient();
        var memory = factory.Services.GetRequiredService<ISceneMemoryStore>();
        var workspace = await memory.CreateAsync("API sequence");
        var request = new StartSceneVideoRun(Guid.NewGuid().ToString(), workspace.Id, "A fox walks across a clearing.", 20, 10, ReviewPlan: true);
        using var response = await http.PostAsJsonAsync("/api/scenarios/scene-video-chain/runs", request);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var run = (await response.Content.ReadFromJsonAsync<AgentRun>())!;
        var repeated = await http.PostAsJsonAsync("/api/scenarios/scene-video-chain/runs", request);
        Assert.Equal(run.Id, (await repeated.Content.ReadFromJsonAsync<AgentRun>())!.Id);
        var mismatch = await http.PostAsJsonAsync("/api/scenarios/scene-video-chain/runs", request with { Prompt = "Different" });
        Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
        for (var i = 0; i < 8 && run.Status != "waiting"; i++) { await factory.Tick(); run = (await http.GetFromJsonAsync<AgentRun>(response.Headers.Location))!; }
        Assert.Equal("waiting", run.Status); Assert.True(run.Question!.AllowFreeText);
        var reply = new { run.Revision, QuestionId = run.Question.Id, Choice = "approve", Text = (string?)null };
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsJsonAsync($"/api/agent-runs/{run.Id}/reply", reply)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await http.PostAsJsonAsync($"/api/agent-runs/{run.Id}/reply", reply)).StatusCode);
        for (var i = 0; i < 20; i++) await factory.Tick();
        run = (await http.GetFromJsonAsync<AgentRun>($"/api/agent-runs/{run.Id}"))!;
        Assert.Equal("audio-review", run.Question!.Kind);
        (await http.PostAsJsonAsync($"/api/agent-runs/{run.Id}/reply", new { run.Revision, QuestionId = run.Question.Id, Choice = "improved" })).EnsureSuccessStatusCode();
        await factory.Tick(); await factory.Tick();
        run = (await http.GetFromJsonAsync<AgentRun>($"/api/agent-runs/{run.Id}"))!;
        Assert.Equal("completed", run.Status);
        Assert.Equal(2, SceneVideoLoop.State(run).Clips.Count(c => c.VideoUrl != null));
        var events = (await http.GetFromJsonAsync<AgentRunEvent[]>($"/api/agent-runs/{run.Id}/events"))!;
        Assert.Contains(events, e => e.Kind == "completed");
        Assert.Empty((await http.GetFromJsonAsync<AgentRunEvent[]>($"/api/agent-runs/{run.Id}/events?after={events.Last().Sequence}"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/api/agent-runs/missing/events")).StatusCode);
        var page = await http.GetStringAsync("/Dashboard/ScenarioRun"); Assert.Contains("vr-question", page);
    }
    [Fact]
    public async Task New_story_is_default_isolated_and_idempotent_across_provisioning_replay()
    {
        await using var factory = new Factory(); using var http = factory.CreateClient();
        var memory = factory.Services.GetRequiredService<ISceneMemoryStore>();
        var existing = await memory.ConnectAsync("Old story", new("cynelo", "test-owner", "old"));
        existing = await memory.SyncStudioAsync(existing.Id, "chipmunk", new("asset", 1, "Ryan", "Character", new Dictionary<string, string>()));
        var request = new { requestId = Guid.NewGuid().ToString(), prompt = "A woman walks along an empty beach.", reviewPlan = true };
        var response = await http.PostAsJsonAsync("/api/scenarios/scene-video-chain/runs", request);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var run = (await response.Content.ReadFromJsonAsync<AgentRun>())!;
        Assert.Equal("initialize", run.Phase); Assert.NotEqual(existing.Id, run.WorkspaceId);
        Assert.Equal("new", SceneVideoLoop.State(run).Input.StoryMode);
        var retry = await http.PostAsJsonAsync("/api/scenarios/scene-video-chain/runs", request);
        Assert.Equal(run.Id, (await retry.Content.ReadFromJsonAsync<AgentRun>())!.Id);
        // Simulate creation succeeding just before a lost worker checkpoint.
        var gateway = factory.Services.GetRequiredService<ISceneVideoStoryGateway>();
        var created = await gateway.EnsureAsync(run.Id, SceneVideoLoop.State(run).StoryName!, default);
        await factory.Tick(); run = (await http.GetFromJsonAsync<AgentRun>(response.Headers.Location))!;
        Assert.Equal("project", run.Phase); Assert.Equal(created.WorkspaceId, run.WorkspaceId);
        var fresh = await memory.GetAsync(run.WorkspaceId);
        Assert.Empty(fresh.Entities); Assert.Empty(fresh.History); Assert.Empty(fresh.Prompts ?? []);
        Assert.Equal(created.StoryId, fresh.Connection!.StoryId);
        Assert.Single((await memory.GetAsync(existing.Id)).Entities);
        Assert.Equal(2, (await gateway.ListAsync(default)).Count);
        // The uniqueness index must now protect the resolved workspace, not the temporary admission ID.
        var conflicting = await http.PostAsJsonAsync("/api/scenarios/scene-video-chain/runs",
            new StartSceneVideoRun(Guid.NewGuid().ToString(), run.WorkspaceId, "Continue the beach story.", StoryMode: "continue"));
        Assert.Equal(HttpStatusCode.Conflict, conflicting.StatusCode);
        for (var i = 0; i < 8 && run.Status != "waiting"; i++) { await factory.Tick(); run = (await http.GetFromJsonAsync<AgentRun>(response.Headers.Location))!; }
        Assert.Equal("review", run.Question!.Kind);
        Assert.Equal(run.Id, (await (await http.PostAsJsonAsync("/api/scenarios/scene-video-chain/runs", request)).Content.ReadFromJsonAsync<AgentRun>())!.Id);
    }
    [Theory]
    [InlineData("new", "existing")]
    [InlineData("continue", null)]
    [InlineData("unexpected", null)]
    public async Task Ambiguous_story_selection_is_rejected_without_provisioning(string mode, string? workspace)
    {
        await using var factory = new Factory(); using var http = factory.CreateClient();
        var response = await http.PostAsJsonAsync("/api/scenarios/scene-video-chain/runs",
            new StartSceneVideoRun(Guid.NewGuid().ToString(), workspace, "A quiet beach.", StoryMode: mode));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await factory.Services.GetRequiredService<ISceneVideoStoryGateway>().ListAsync(default));
    }
    [Fact]
    public async Task Different_new_requests_receive_different_empty_stories()
    {
        await using var factory = new Factory(); using var http = factory.CreateClient();
        var ids = new List<string>();
        for (var i = 0; i < 2; i++) {
            var response = await http.PostAsJsonAsync("/api/scenarios/scene-video-chain/runs",
                new { requestId = Guid.NewGuid().ToString(), storyMode = "new", prompt = "A quiet beach.", reviewPlan = true });
            var run = (await response.Content.ReadFromJsonAsync<AgentRun>())!;
            var store = factory.Services.GetRequiredService<IAgentRunStore>();
            var lease = (await store.ClaimAsync(TimeSpan.FromMinutes(1)))!;
            Assert.Equal(run.Id, lease.Run.Id);
            await factory.Services.GetRequiredService<SceneVideoLoop>().StepAsync(lease, default);
            run = (await store.GetAsync(run.Id))!; ids.Add(run.WorkspaceId);
            await store.CancelAsync(run.Id);
        }
        Assert.Equal(2, ids.Distinct().Count());
        Assert.Equal(2, (await http.GetFromJsonAsync<SceneVideoStory[]>("/api/agent-runs/stories"))!.Length);
    }
    private sealed class Stories(ISceneMemoryStore memory) : ISceneVideoStoryGateway
    {
        public async Task<SceneVideoStory> EnsureAsync(string key, string name, CancellationToken ct) {
            var workspace = await memory.ConnectAsync(name, new("cynelo", "test-owner", "sequence-" + key), cancellationToken: ct);
            return new(workspace.Id, workspace.Connection!.StoryId, workspace.Name);
        }
        public async Task<IReadOnlyList<SceneVideoStory>> ListAsync(CancellationToken ct) =>
            (await memory.ListAsync(ct)).Where(w => w.Connection != null).Select(w => new SceneVideoStory(w.Id, w.Connection!.StoryId, w.Name)).ToArray();
    }
    private sealed class Factory : AgctorWebApplicationFactory
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "agctor-video-api-" + Guid.NewGuid().ToString("N"));
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> {
                ["Agctor:SceneVideo:WorkerEnabled"] = "false", ["Agctor:SceneVideo:StorePath"] = Path.Combine(dir, "runs.db"),
                ["Agctor:SceneMemory:StorePath"] = Path.Combine(dir, "scene.db") }));
            builder.ConfigureServices(services => {
                services.AddSingleton<ISceneMemoryStore>(new SqliteSceneMemoryStore(Path.Combine(dir, "isolated-scene.db")));
                services.AddSingleton<IAgentRunStore>(new SqliteAgentRunStore(Path.Combine(dir, "isolated-runs.db")));
                services.AddSingleton<ISceneVideoPlanner, Planner>();
                services.AddSingleton<IScenePromptInterpreter, Interpreter>(); services.AddSingleton<ISceneVideoRenderer, Renderer>();
                services.AddSingleton<ISceneVideoStoryGateway, Stories>(); services.AddSingleton<ISceneVideoProduction, Production>(); services.AddSingleton<ISceneVideoCritic, Critic>(); services.AddSingleton<ISceneAudioEngineer, Audio>(); });
        }
        public async Task Tick()
        {
            var store = Services.GetRequiredService<IAgentRunStore>(); var lease = await store.ClaimAsync(TimeSpan.FromMinutes(1));
            if (lease != null) await Services.GetRequiredService<SceneVideoLoop>().StepAsync(lease, default);
        }
    }
    private sealed class Planner : ISceneVideoPlanner
    {
        public Task<SceneVideoPlan> PlanAsync(SceneWorkspace w, SceneVideoState s, IReadOnlyList<int> durations, CancellationToken ct) =>
            Task.FromResult(new SceneVideoPlan(durations.Select((_, i) => "The fox advances " + (i + 1)).ToArray()));
    }
    private sealed class Interpreter : IScenePromptInterpreter
    {
        public Task<ScenePromptExtraction> InterpretAsync(SceneWorkspace w, string p, string m, CancellationToken ct) =>
            Task.FromResult(new ScenePromptExtraction([], [], []));
    }
    private sealed class Renderer : ISceneVideoRenderer
    {
        public bool IsConfigured => true;
        public Task<SceneRenderJob> EnsureAsync(string key, SceneRenderRequest request, CancellationToken ct) =>
            Task.FromResult(new SceneRenderJob(key, "completed", "https://example.test/video.mp4", "https://example.test/frame.jpg"));
        public Task<SceneRenderJob> GetAsync(string key, CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class Production : ISceneVideoProduction {
        public Task<SceneVideoProject> EnsureProjectAsync(string run, string story, string mode, string name, CancellationToken ct, string? projectId = null) => Task.FromResult(new SceneVideoProject(projectId ?? "project"));
        public Task<SceneVideoEvidence> EvidenceAsync(string key, CancellationToken ct) => Task.FromResult(new SceneVideoEvidence(["a", "b", "c"]));
        public Task<SceneAudioDraft> PrepareAudioAsync(string run, string project, IReadOnlyList<string> keys, SceneAudioPlan plan, CancellationToken ct) => Task.FromResult(new SceneAudioDraft(true, "Audio ready", JsonSerializer.SerializeToElement(new { })));
        public Task<SceneVideoDelivery> FinishAudioAsync(string run, string project, IReadOnlyList<string> keys, string choice, CancellationToken ct) => FinishAsync(run, project, keys, ct);
        public Task<SceneVideoDelivery> FinishAsync(string run, string project, IReadOnlyList<string> keys, CancellationToken ct) => Task.FromResult(new SceneVideoDelivery("https://example.test/finished.mp4"));
    }
    private sealed class Audio : ISceneAudioEngineer {
        public Task<SceneAudioPlan> PlanAsync(string prompt, string model, CancellationToken ct) => Task.FromResult(new SceneAudioPlan(false, "Balance original audio"));
        public Task<SceneContinuityReport> ReviewAsync(string prompt, string model, SceneAudioDraft draft, CancellationToken ct) => Task.FromResult(new SceneContinuityReport("passed", "Measured levels checked", []));
    }
    private sealed class Critic : ISceneVideoCritic {
        public Task<SceneContinuityReport> CheckAsync(string prompt, SceneVideoEvidence current, SceneVideoEvidence? previous, CancellationToken ct) => Task.FromResult(new SceneContinuityReport("passed", "Sampled frames match", []));
    }
}
