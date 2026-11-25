using System.Net;
using System.Text;
using System.Text.Json;
using AgctorSDK.Core.Workflows;
using AgctorSDK.Host.Services.VideoWorkflow;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class SceneVideoTraceTests : IDisposable
{
    private readonly string file = Path.Combine(Path.GetTempPath(), "trace-" + Guid.NewGuid().ToString("N") + ".db");
    private SqliteAgentRunStore Store => new(file);
    private async Task<AgentRunLease> Lease(string workspace)
    {
        await Store.CreateAsync(new() { WorkspaceId = workspace, RequestId = workspace, RequestHash = workspace,
            State = JsonSerializer.SerializeToElement(new { }) });
        return (await Store.ClaimAsync(TimeSpan.FromMinutes(1)))!;
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request);
    }
    [Fact]
    public async Task Actual_model_is_paired_with_response_without_recording_prompt_or_url()
    {
        var lease = await Lease("one");
        var payload = "{\"model\":\"vision:latest\",\"prompt\":\"private-story\"}";
        using var http = new HttpClient(new SceneVideoTraceHandler("ollama") { InnerHandler = new Handler(async req => {
            Assert.Equal(payload, await req.Content!.ReadAsStringAsync());
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"response\":\"secret-answer\"}") };
        }) });
        using (SceneVideoTrace.Begin(Store, lease, SceneVideoRoles.VisualCritic, NullLogger.Instance))
            await http.PostAsync("http://private-server/api/generate", new StringContent(payload));
        Assert.False(SceneVideoTrace.IsActive);
        var routes = (await Store.EventsAsync(lease.Run.Id)).Where(e=>e.Route!=null).ToArray();
        Assert.Equal(2, routes.Length);
        Assert.Equal("Ollama · vision:latest", routes[0].Route!.To);
        Assert.Equal("llm", routes[0].Route!.ToKind);
        Assert.Equal("vision:latest", routes[0].Route!.ToDetail);
        Assert.Equal(SceneVideoRoles.VisualCritic, routes[1].Route!.To);
        Assert.Equal(routes[0].Route!.ExchangeId, routes[1].Route!.ExchangeId);
        Assert.Equal("completed", routes[1].Route!.Outcome);
        Assert.True(routes[1].Route!.DurationMs >= 0);
        var saved=JsonSerializer.Serialize(routes);
        foreach(var secret in new[]{"private-story","private-server","secret-answer",lease.Token}) Assert.DoesNotContain(secret,saved);
        await http.PostAsync("http://private-server/api/generate",new StringContent(payload));
        Assert.Equal(2,(await Store.EventsAsync(lease.Run.Id)).Count(e=>e.Route!=null));
    }
    [Fact]
    public async Task Concurrent_scopes_are_isolated_and_tool_failures_remain_failures()
    {
        var a=await Lease("a"); var b=await Lease("b");
        async Task Work(AgentRunLease lease,string role,string tool) {
            using var scope=SceneVideoTrace.Begin(Store,lease,role,NullLogger.Instance);
            await Assert.ThrowsAsync<IOException>(()=>SceneVideoTrace.InvokeAsync<int>(tool,"tool","Cynelo API","Check",async()=>{
                await Task.Delay(10); throw new IOException("private exception"); }));
        }
        // Each role uses a tool it owns (Phase B matrix).
        await Task.WhenAll(Work(a,"Render agent","Video rendering"),Work(b,"Audio Engineer","Audio production"));
        foreach(var pair in new[]{(a,"Render agent"),(b,"Audio Engineer")}) {
            var routes=(await Store.EventsAsync(pair.Item1.Run.Id)).Where(e=>e.Route!=null).ToArray();
            Assert.Equal(2,routes.Length); Assert.Equal(pair.Item2,routes[0].Route!.From);
            Assert.Equal("failed",routes[1].Route!.Outcome); Assert.DoesNotContain("private exception",JsonSerializer.Serialize(routes));
        }
        Assert.False(SceneVideoTrace.IsActive);
    }
    [Fact]
    public async Task Trace_failure_never_retries_or_discards_successful_operation()
    {
        var lease=await Lease("cancel"); await Store.CancelAsync(lease.Run.Id); var called=0;
        using var scope=SceneVideoTrace.Begin(Store,lease,"Editor agent",NullLogger.Instance);
        var result=await SceneVideoTrace.InvokeAsync("Film assembly","tool","Cynelo API","Finish",()=>Task.FromResult(++called));
        Assert.Equal(1,result); Assert.Equal(1,called);
    }
    [Fact]
    public async Task Topology_retains_model_after_paged_history_and_restart_and_is_run_scoped()
    {
        var lease=await Lease("history");
        using (SceneVideoTrace.Begin(Store,lease,"Planner",NullLogger.Instance))
            await SceneVideoTrace.InvokeAsync("Ollama · planning:8b","llm","planning:8b","Plan",()=>Task.FromResult(1));
        for(var i=0;i<220;i++) await Store.ActivityAsync(lease,"Render agent","Polling",route:new("Director","Render agent","poll",Guid.NewGuid().ToString()));
        var other=await Lease("other");
        var graph=await new SqliteAgentRunStore(file).GraphAsync(lease.Run.Id);
        Assert.Contains(graph.Nodes,n=>n.Id=="Ollama · planning:8b"&&n.Kind=="llm"&&n.Detail=="planning:8b");
        Assert.Equal(220,graph.Edges.Single(e=>e.From=="Director").Count);
        Assert.Empty((await Store.GraphAsync(other.Run.Id)).Nodes);
    }
    [Fact]
    public async Task Downstream_model_evidence_is_explicitly_reported_not_an_invented_live_call()
    {
        var lease=await Lease("audio");
        using var http=new HttpClient(new SceneVideoTraceHandler("cynelo") { InnerHandler=new Handler(_=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
            Content=new StringContent("{\"evidence\":{\"separator\":{\"model\":\"roformer\"},\"narration\":[{\"model\":\"kokoro\"}]}}",Encoding.UTF8,"application/json") })) });
        using(SceneVideoTrace.Begin(Store,lease,"Audio Engineer",NullLogger.Instance))
            await http.PostAsync("http://cynelo/api/sequence/audio",new StringContent("{}"));
        var routes=(await Store.EventsAsync(lease.Run.Id)).Where(e=>e.Route!=null).Select(e=>e.Route!).ToArray();
        Assert.Equal(4,routes.Length);
        Assert.All(routes.Skip(2),r=>{Assert.Equal("reported",r.Outcome);Assert.Equal("model",r.ToKind);Assert.Equal("Audio production",r.From);});
    }
    [Fact]
    public async Task Pending_call_survives_history_paging_and_store_reopen_and_clears_on_response()
    {
        var lease=await Lease("pending");
        var release=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int> work;
        using(SceneVideoTrace.Begin(Store,lease,"Planner",NullLogger.Instance))
            work=SceneVideoTrace.InvokeAsync("Ollama · gemma4:latest","llm","gemma4:latest","Request model response",()=>release.Task);
        try {
            for(var i=0;i<220;i++) await Store.ActivityAsync(lease,"Director","Other diagnostic event");
            var reopened=new SqliteAgentRunStore(file);
            var call=Assert.Single((await reopened.GetAsync(lease.Run.Id))!.ActiveCalls!);
            Assert.Equal("Planner",call.From); Assert.Equal("Ollama · gemma4:latest",call.To);
            Assert.True(call.ConfirmedUntil>call.StartedAt);
            Assert.DoesNotContain(lease.Token,JsonSerializer.Serialize(call));
            Assert.Single((await reopened.GraphAsync(lease.Run.Id)).ActiveCalls!);
            Assert.True(await reopened.RenewAsync(lease,TimeSpan.FromMinutes(2)));
            var renewed=Assert.Single((await reopened.GetAsync(lease.Run.Id))!.ActiveCalls!);
            Assert.Equal(call.StartedAt,renewed.StartedAt); Assert.True(renewed.ConfirmedUntil>call.ConfirmedUntil);
        } finally { release.SetResult(1); await work; }
        Assert.Empty((await Store.GetAsync(lease.Run.Id))!.ActiveCalls!);
    }
    [Fact]
    public async Task Mailbox_isolation_blocks_cross_role_tools()
    {
        var lease = await Lease("ban");
        using (SceneVideoTrace.Begin(Store, lease, SceneVideoRoles.Director, NullLogger.Instance))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                SceneVideoTrace.InvokeAsync("Audio production", "tool", "Cynelo API", "Prepare", () => Task.FromResult(1)));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                SceneVideoTrace.InvokeAsync("Video rendering", "tool", "Cynelo API", "Render", () => Task.FromResult(1)));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                SceneVideoTrace.InvokeAsync("Ollama · x", "llm", "x", "Plan", () => Task.FromResult(1)));
        }
        using (SceneVideoTrace.Begin(Store, lease, SceneVideoRoles.Render, NullLogger.Instance))
        {
            Assert.Equal(2, await SceneVideoTrace.InvokeAsync("Video rendering", "tool", "Cynelo API", "Render", () => Task.FromResult(2)));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                SceneVideoTrace.InvokeAsync("Film assembly", "tool", "Cynelo API", "Finish", () => Task.FromResult(1)));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                SceneVideoTrace.InvokeAsync("Audio production", "tool", "Cynelo API", "Mix", () => Task.FromResult(1)));
        }
        using (SceneVideoTrace.Begin(Store, lease, SceneVideoRoles.Planner, NullLogger.Instance))
            Assert.Equal(5, await SceneVideoTrace.InvokeAsync("Ollama · gemma4:latest", "llm", "gemma4:latest", "Plan", () => Task.FromResult(5)));
        using (SceneVideoTrace.Begin(Store, lease, SceneVideoRoles.AudioEngineer, NullLogger.Instance))
            Assert.Equal(7, await SceneVideoTrace.InvokeAsync("Audio production", "tool", "Cynelo API", "Prepare", () => Task.FromResult(7)));
        using (SceneVideoTrace.Begin(Store, lease, SceneVideoRoles.Director, NullLogger.Instance))
        {
            using (SceneVideoTrace.SwitchRole(SceneVideoRoles.DialogueSupervisor))
                Assert.Equal(3, await SceneVideoTrace.InvokeAsync("Speech inspection", "tool", "Cynelo API", "Inspect", () => Task.FromResult(3)));
        }
        Assert.DoesNotContain(await Store.EventsAsync(lease.Run.Id),
            e => e.Route is { From: "Director", To: "Audio production" or "Speech inspection" or "Voice synthesis" or "Video rendering" });
    }
    [Fact]
    public async Task Audio_discovery_endpoints_trace_to_audio_production_under_audio_engineer()
    {
        var lease = await Lease("audio-discovery");
        using var http = new HttpClient(new SceneVideoTraceHandler("cynelo") { InnerHandler = new Handler(req => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    req.RequestUri!.AbsolutePath.EndsWith("/narration-models") ? "[\"kokoro\",\"eleven_multilingual_v2\"]" : "true",
                    Encoding.UTF8, "application/json")
            })) });

        using (SceneVideoTrace.Begin(Store, lease, SceneVideoRoles.VoiceActor, NullLogger.Instance))
            Assert.Equal("[\"kokoro\",\"eleven_multilingual_v2\"]",
                await (await http.GetAsync("http://cynelo/api/agctor/render/narration-models")).Content.ReadAsStringAsync());

        using (SceneVideoTrace.Begin(Store, lease, SceneVideoRoles.Director, NullLogger.Instance))
        using (SceneVideoTrace.SwitchRole(SceneVideoRoles.AudioEngineer))
            Assert.Equal("true",
                await (await http.GetAsync("http://cynelo/api/agctor/render/matching-music-available")).Content.ReadAsStringAsync());

        using (SceneVideoTrace.Begin(Store, lease, SceneVideoRoles.Render, NullLogger.Instance))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => http.GetAsync("http://cynelo/api/agctor/render/matching-music-available"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => http.GetAsync("http://cynelo/api/agctor/render/narration-models"));
        }

        var routes = (await Store.EventsAsync(lease.Run.Id)).Where(e => e.Route != null).Select(e => e.Route!).ToArray();
        Assert.Equal(4, routes.Length);
        Assert.Contains(routes, r => r.From == SceneVideoRoles.VoiceActor && r.To == "Voice synthesis" && r.Operation == "Check available narration voices" && r.Outcome == "started");
        Assert.Contains(routes, r => r.From == SceneVideoRoles.AudioEngineer && r.To == "Audio production" && r.Operation == "Check matching music availability" && r.Outcome == "started");
        Assert.DoesNotContain(routes, r => r.To == "Video rendering");
    }
    [Fact]
    public async Task Cue_sync_and_timeline_endpoints_enforce_visual_critic_and_audio_engineer_boundaries()
    {
        var lease = await Lease("cue-timeline");
        using var http = new HttpClient(new SceneVideoTraceHandler("cynelo") { InnerHandler = new Handler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            })) });

        using (SceneVideoTrace.Begin(Store, lease, SceneVideoRoles.VisualCritic, NullLogger.Instance))
            Assert.True((await http.GetAsync("http://cynelo/api/agctor/render/jobs/run-1-1/cue-frames")).IsSuccessStatusCode);

        using (SceneVideoTrace.Begin(Store, lease, SceneVideoRoles.AudioEngineer, NullLogger.Instance))
        {
            Assert.True((await http.PutAsync("http://cynelo/api/agctor/render/sequences/run-1/timeline",
                new StringContent("{}", Encoding.UTF8, "application/json"))).IsSuccessStatusCode);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                http.GetAsync("http://cynelo/api/agctor/render/jobs/run-1-1/cue-frames"));
        }

        using (SceneVideoTrace.Begin(Store, lease, SceneVideoRoles.VisualCritic, NullLogger.Instance))
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                http.PutAsync("http://cynelo/api/agctor/render/sequences/run-1/timeline",
                    new StringContent("{}", Encoding.UTF8, "application/json")));

        var routes = (await Store.EventsAsync(lease.Run.Id)).Where(e => e.Route != null).Select(e => e.Route!).ToArray();
        Assert.Contains(routes, r => r.From == SceneVideoRoles.VisualCritic && r.To == "Frame sampler" && r.Outcome == "started");
        Assert.Contains(routes, r => r.From == SceneVideoRoles.AudioEngineer && r.To == "Audio production" && r.Outcome == "started");
    }
    [Theory]
    [InlineData("save")]
    [InlineData("cancel")]
    [InlineData("expire")]
    public async Task Interrupted_or_finished_work_is_never_shown_as_pending(string end)
    {
        var lease=await Lease("lifecycle");
        await Store.ActivityAsync(lease,"Planner","Started",route:new("Planner","Model","plan","pending",Operation:"Plan",Outcome:"started"));
        Assert.Single((await Store.GetAsync(lease.Run.Id))!.ActiveCalls!);
        if(end=="save") await Store.SaveAsync(lease,"Planner","completed","Plan done");
        else if(end=="cancel") await Store.CancelAsync(lease.Run.Id);
        else {
            using var db=new Microsoft.Data.Sqlite.SqliteConnection("Data Source="+file); db.Open();
            using var command=db.CreateCommand(); command.CommandText="UPDATE agent_runs SET lease_until=0"; command.ExecuteNonQuery();
        }
        Assert.Empty((await Store.GraphAsync(lease.Run.Id)).ActiveCalls!);
        if(end!="cancel") {
            var next=(await Store.ClaimAsync(TimeSpan.FromMinutes(1)))!;
            Assert.Empty((await Store.GetAsync(next.Run.Id))!.ActiveCalls!);
        }
    }
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); foreach(var path in new[]{file,file+"-shm",file+"-wal"}) if(File.Exists(path)) File.Delete(path); }
}
