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
        using (SceneVideoTrace.Begin(Store, lease, "Continuity critic", NullLogger.Instance))
            await http.PostAsync("http://private-server/api/generate", new StringContent(payload));
        Assert.False(SceneVideoTrace.IsActive);
        var routes = (await Store.EventsAsync(lease.Run.Id)).Where(e=>e.Route!=null).ToArray();
        Assert.Equal(2, routes.Length);
        Assert.Equal("Ollama · vision:latest", routes[0].Route!.To);
        Assert.Equal("llm", routes[0].Route!.ToKind);
        Assert.Equal("vision:latest", routes[0].Route!.ToDetail);
        Assert.Equal("Continuity critic", routes[1].Route!.To);
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
        async Task Work(AgentRunLease lease,string role) {
            using var scope=SceneVideoTrace.Begin(Store,lease,role,NullLogger.Instance);
            await Assert.ThrowsAsync<IOException>(()=>SceneVideoTrace.InvokeAsync<int>("Video rendering","tool","Cynelo API","Check render",async()=>{
                await Task.Delay(10); throw new IOException("private exception"); }));
        }
        await Task.WhenAll(Work(a,"Render agent"),Work(b,"Audio Engineer"));
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
