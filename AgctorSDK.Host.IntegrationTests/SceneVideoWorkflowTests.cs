using System.Text.Json;
using AgctorSDK.Core.Adapters;
using AgctorSDK.Core.SceneMemory;
using AgctorSDK.Core.Workflows;
using AgctorSDK.Host.Services;
using AgctorSDK.Host.Services.VideoWorkflow;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class SceneVideoWorkflowTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "agctor-video-test-" + Guid.NewGuid().ToString("N"));
    private readonly SqliteAgentRunStore runs;
    private readonly SqliteSceneMemoryStore memory;
    private readonly Planner planner = new();
    private readonly Interpreter interpreter = new();
    private readonly Renderer renderer = new();
    private readonly Monitor options = new();
    public SceneVideoWorkflowTests()
    { Directory.CreateDirectory(dir); runs = new(Path.Combine(dir, "runs.db")); memory = new(Path.Combine(dir, "scene.db")); }
    private SceneVideoLoop Loop() => new(runs, memory, planner, interpreter, renderer, options, NullLogger<SceneVideoLoop>.Instance, production: production, critic: critic, audio: audio);
    private async Task<AgentRun> Create(bool review = false, int seconds = 31)
    {
        var workspace = await memory.CreateAsync("Test sequence");
        return await runs.CreateAsync(new() { RequestId = Guid.NewGuid().ToString(), RequestHash = "same", WorkspaceId = workspace.Id,
            Workflow = "scene-video", ScenarioId = "scene-video-chain", State = JsonSerializer.SerializeToElement(new SceneVideoState {
                Input = new("test", workspace.Id, "A traveller crosses a bridge and arrives home.", seconds, 15, ReviewPlan: review),
                WorkspaceRevision = ScenePromptReducer.Revision(workspace), Model = "fake" }) });
    }
    private async Task<AgentRun> Tick(string id)
    {
        var lease = await runs.ClaimAsync(TimeSpan.FromMinutes(1), monitors: SceneVideoLoop.WaitMonitors); Assert.NotNull(lease);
        Assert.Equal(id, lease.Run.Id); await Loop().StepAsync(lease, default);
        return (await runs.GetAsync(id))!;
    }
    [Fact]
    public async Task Three_clips_preserve_order_durations_memory_and_continuation_frames()
    {
        var run = await Create();
        for (var step = 0; step < 20 && !run.IsTerminal; step++) run = await Tick(run.Id);
        Assert.Equal("completed", run.Status);
        Assert.Equal(new[] { 15, 15, 1 }, renderer.Requests.Select(r => r.DurationSeconds));
        Assert.Null(renderer.Requests[0].FirstFrameUrl);
        Assert.Equal("https://cynelo.test/frame/1.jpg", renderer.Requests[1].FirstFrameUrl);
        Assert.Equal("https://cynelo.test/frame/2.jpg", renderer.Requests[2].FirstFrameUrl);
        Assert.Equal(new[] { 0, 1, 2 }, interpreter.HistorySizes);
        Assert.Equal(3, (await memory.GetAsync(run.WorkspaceId)).Prompts!.Count);
        Assert.Contains(await runs.EventsAsync(run.Id), e => e.Agent == "Director" && e.Message.Contains("Routing"));
        var reopened = new SqliteAgentRunStore(Path.Combine(dir, "runs.db"));
        Assert.Equal("completed", (await reopened.GetAsync(run.Id))!.Status);
    }
    [Fact]
    public async Task Sequence_only_sends_designs_for_actual_participants_to_renderer()
    {
        var workspace = await memory.ConnectAsync("Story with available designs", new("cynelo", "test", "story"));
        workspace = await memory.SyncStudioAsync(workspace.Id, "traveller", new("traveller", 1, "Traveller", "Character",
            new Dictionary<string, string> { ["appearance"] = "Red coat" }));
        foreach (var name in new[] { "Ryan", "Raha", "Sword", "Magical Dragon" })
            workspace = await memory.SyncStudioAsync(workspace.Id, name, new(name, 1, name,
                name is "Ryan" or "Raha" ? "Character" : "Object", new Dictionary<string, string> { ["appearance"] = "Unrelated " + name }));
        var run = await runs.CreateAsync(new() { RequestId = Guid.NewGuid().ToString(), RequestHash = "scope", WorkspaceId = workspace.Id,
            Workflow = "scene-video", ScenarioId = "scene-video-chain", State = JsonSerializer.SerializeToElement(new SceneVideoState {
                Input = new("test", workspace.Id, "The traveller walks through a forest toward a bridge.", 30, 15),
                WorkspaceRevision = ScenePromptReducer.Revision(workspace), Model = "fake" }) });
        for (var step = 0; step < 16 && !run.IsTerminal; step++) run = await Tick(run.Id);
        Assert.Equal("completed", run.Status); Assert.Equal(2, renderer.Requests.Count);
        Assert.All(renderer.Requests, request => {
            Assert.Contains("Red coat", request.Prompt);
            foreach (var unrelated in new[] { "Ryan", "Raha", "Sword", "Magical Dragon" }) Assert.DoesNotContain(unrelated, request.Prompt);
        });
        Assert.All(interpreter.ContextNames, names => Assert.Equal(new[] { "Traveller" }, names));
        Assert.Equal(new[] { "Traveller" }, planner.ContextNames);
        Assert.Equal(5, (await memory.GetAsync(workspace.Id)).Entities.Count); // Availability was not deleted to achieve isolation.
    }
    [Fact]
    public async Task Reference_selection_is_reported_and_survives_restart()
    {
        renderer.ReportQueued = true;
        var run = await Create(seconds: 15);
        for (var i = 0; i < 4; i++) run = await Tick(run.Id);
        Assert.Equal("poll", run.Phase);
        Assert.Contains("8 of 10 reference images across 5 designs", run.Summary);
        var reopened = new SqliteAgentRunStore(Path.Combine(dir, "runs.db"));
        var clip = Assert.Single(SceneVideoLoop.State((await reopened.GetAsync(run.Id))!).Clips);
        Assert.Equal(new SceneRenderReferences(8, 10, 5), clip.References);
    }
    [Fact]
    public async Task Human_review_accepts_free_text_replans_and_requires_explicit_approval()
    {
        var run = await Tick((await Create(review: true)).Id);
        Assert.Equal("waiting", run.Status); Assert.Equal("review", run.Question!.Kind);
        var oldRevision = run.Revision; var oldQuestion = run.Question.Id;
        run = await runs.ReplyAsync(run.Id, run.Revision, new(oldQuestion, null, "Make the ending joyful"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => runs.ReplyAsync(run.Id, oldRevision, new(oldQuestion, "approve", null)));
        run = await Tick(run.Id); Assert.Equal("plan", run.Phase);
        run = await Tick(run.Id); Assert.Equal("waiting", run.Status);
        Assert.Contains("joyful", planner.Clarifications!);
        run = await runs.ReplyAsync(run.Id, run.Revision, new(run.Question!.Id, "approve", null));
        run = await Tick(run.Id); Assert.Equal("prepare", run.Phase); Assert.Null(run.Question);
        Assert.True(SceneVideoLoop.State(run).PlanReviewed); Assert.Empty(renderer.Requests);
    }
    [Fact]
    public async Task Crash_after_scene_commit_replays_without_duplicate_shot()
    {
        var run = await Tick((await Create(seconds: 15)).Id); run = await Tick(run.Id);
        Assert.Equal("commit", run.Phase);
        await memory.ApplyPromptAsync(run.WorkspaceId, SceneVideoLoop.State(run).Clips[0].Preview!);
        run = await Tick(run.Id);
        Assert.Equal("submit", run.Phase); Assert.Single((await memory.GetAsync(run.WorkspaceId)).Prompts!);
        run = await Tick(run.Id); Assert.Equal("completed", run.Status);
    }
    [Fact]
    public async Task Lost_render_response_reuses_the_same_key_after_human_retry()
    {
        var run = await Create(seconds: 15);
        var state = SceneVideoLoop.State(run); state.MaxAttempts = 1;
        var lease = (await runs.ClaimAsync(TimeSpan.FromMinutes(1)))!;
        lease.Run.State = JsonSerializer.SerializeToElement(state); await runs.SaveAsync(lease, "test", "setup", "Ready");
        run = await Tick(run.Id); run = await Tick(run.Id); run = await Tick(run.Id);
        renderer.LoseResponse = true; run = await Tick(run.Id);
        Assert.Equal("waiting", run.Status); Assert.Single(renderer.Keys);
        run = await runs.ReplyAsync(run.Id, run.Revision, new(run.Question!.Id, "retry", "Connection restored"));
        await Tick(run.Id); renderer.LoseResponse = false; run = await Tick(run.Id);
        Assert.Equal("completed", run.Status); Assert.Equal(2, renderer.Keys.Count);
        Assert.Equal(renderer.Keys[0], renderer.Keys[1]);
    }
    [Fact]
    public async Task Cancel_invalidates_worker_lease_and_releases_workspace()
    {
        var run = await Create(); var lease = (await runs.ClaimAsync(TimeSpan.FromMinutes(1)))!;
        Assert.Null(await runs.ClaimAsync(TimeSpan.FromMinutes(1)));
        await runs.CancelAsync(run.Id);
        Assert.False(await runs.RenewAsync(lease, TimeSpan.FromMinutes(1)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => runs.SaveAsync(lease, "worker", "done", "Stale write"));
        Assert.Equal("cancelled", (await runs.GetAsync(run.Id))!.Status);
    }
    [Fact]
    public async Task Admission_is_idempotent_and_blocks_concurrent_scene_writers()
    {
        var run = await Create();
        Assert.Equal(run.Id, (await runs.CreateAsync(run)).Id);
        run.RequestHash = "different";
        await Assert.ThrowsAsync<InvalidOperationException>(() => runs.CreateAsync(run));
        run.RequestId = "other"; run.Id = Guid.NewGuid().ToString("N");
        await Assert.ThrowsAsync<InvalidOperationException>(() => runs.CreateAsync(run));
    }
    [Fact]
    public async Task Missing_renderer_pauses_without_claiming_completion()
    {
        renderer.IsConfigured = false;
        var run = await Create(seconds: 15);
        for (var i = 0; i < 4; i++) run = await Tick(run.Id);
        Assert.Equal("waiting", run.Status); Assert.Empty(renderer.Requests); Assert.Contains("not connected", run.Question!.Prompt);
    }
    [Fact]
    public async Task External_memory_change_stops_rendering()
    {
        var run = await Create(seconds: 15);
        for (var i = 0; i < 3; i++) run = await Tick(run.Id);
        await memory.UpdatePromptMetadataAsync(run.WorkspaceId, SceneVideoLoop.State(run).Clips[0].ShotId, "Changed", null, null);
        run = await Tick(run.Id);
        Assert.Equal("conflict", run.Question!.Kind); Assert.Empty(renderer.Requests);
    }
    [Fact]
    public async Task Expired_lease_can_be_reclaimed_but_old_worker_cannot_save()
    {
        var run = await Create(); var stale = (await runs.ClaimAsync(TimeSpan.FromMilliseconds(-1)))!;
        var replacement = (await runs.ClaimAsync(TimeSpan.FromMinutes(1)))!;
        Assert.Equal(run.Id, replacement.Run.Id); Assert.NotEqual(stale.Token, replacement.Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => runs.SaveAsync(stale, "old", "done", "Stale worker"));
        await runs.SaveAsync(replacement, "new", "resumed", "Recovered");
        Assert.Equal("Recovered", (await runs.GetAsync(run.Id))!.Summary);
    }
    [Fact]
    public async Task External_edit_after_interrupted_commit_is_not_absorbed_into_the_render()
    {
        var run = await Tick((await Create(seconds: 15)).Id); run = await Tick(run.Id);
        var clip = SceneVideoLoop.State(run).Clips[0];
        await memory.ApplyPromptAsync(run.WorkspaceId, clip.Preview!);
        await memory.UpdatePromptMetadataAsync(run.WorkspaceId, clip.ShotId, "Different scene", null, null);
        run = await Tick(run.Id);
        Assert.Equal("waiting", run.Status); Assert.Empty(renderer.Requests);
    }
    [Fact]
    public async Task Director_and_specialist_execute_through_actor_mailboxes()
    {
        var run = await Create(review: true);
        using var runtime = new InMemoryActorRuntime(); await runtime.InitializeAsync(new Dictionary<string, object>());
        var worker = new SceneVideoWorker(runs, Loop(), runtime, options, NullLogger<SceneVideoWorker>.Instance);
        await worker.ExecuteLeaseAsync((await runs.ClaimAsync(TimeSpan.FromMinutes(1)))!, default);
        var saved = (await runs.GetAsync(run.Id))!;
        Assert.Equal("waiting", saved.Status);
        // Phase C: full run-scoped roster stays registered while the run is not terminal.
        var active = (await runtime.GetActiveActorIdsAsync()).ToList();
        foreach (var role in SceneVideoRoles.Agents)
            Assert.Contains(SceneVideoWorker.ActorId(run.Id, role), active);
        Assert.DoesNotContain(active, id => id.Contains("scene-specialist-", StringComparison.Ordinal));
        await runtime.ShutdownAsync();
    }

    [Fact]
    public async Task Run_scoped_specialists_survive_across_ticks()
    {
        var run = await Create(review: true);
        using var runtime = new InMemoryActorRuntime(); await runtime.InitializeAsync(new Dictionary<string, object>());
        var worker = new SceneVideoWorker(runs, Loop(), runtime, options, NullLogger<SceneVideoWorker>.Instance);
        await worker.ExecuteLeaseAsync((await runs.ClaimAsync(TimeSpan.FromMinutes(1)))!, default);
        Assert.NotNull(await runtime.GetActorAsync<SceneVideoDirectorActor>(SceneVideoWorker.DirectorId(run.Id)));
        Assert.NotNull(await runtime.GetActorAsync<SceneVideoRoleActor>(SceneVideoWorker.AudioId(run.Id)));
        Assert.NotNull(await runtime.GetActorAsync<SceneVideoRoleActor>(SceneVideoWorker.ActorId(run.Id, SceneVideoRoles.Planner)));
        // Same runtime, second claim: Ensure must reuse the run-scoped mailboxes.
        run = (await runs.GetAsync(run.Id))!;
        await runs.ReplyAsync(run.Id, run.Revision, new(run.Question!.Id, "approve", null));
        await worker.ExecuteLeaseAsync((await runs.ClaimAsync(TimeSpan.FromMinutes(1)))!, default);
        Assert.NotNull(await runtime.GetActorAsync<SceneVideoDirectorActor>(SceneVideoWorker.DirectorId(run.Id)));
        Assert.NotNull(await runtime.GetActorAsync<SceneVideoRoleActor>(SceneVideoWorker.ActorId(run.Id, SceneVideoRoles.VisualCritic)));
        await runtime.ShutdownAsync();
    }
    private async Task<AgentRun> WaitingRun(int clips = 2, string kind = "timeout", bool waiting = true)
    {
        var workspace = await memory.CreateAsync("Waiting render");
        return await runs.CreateAsync(new() { RequestId = Guid.NewGuid().ToString(), RequestHash = "waiting", WorkspaceId = workspace.Id,
            Workflow = "scene-video", ScenarioId = "scene-video-chain", Phase = "poll", Status = waiting ? "waiting" : "running",
            Question = waiting ? new("original-question", kind, "Keep waiting?", [new("wait", "Keep waiting")]) : null,
            State = JsonSerializer.SerializeToElement(new SceneVideoState {
                Input = new("waiting", workspace.Id, "A traveller arrives home.", clips * 15, 15),
                WorkspaceRevision = ScenePromptReducer.Revision(workspace), Model = "fake",
                Clips = Enumerable.Range(0, clips).Select(i => new SceneVideoClip { ShotId = "shot-" + i,
                    DurationSeconds = 15, Status = i == 0 ? "rendering" : "planned",
                    ProviderJobId = i == 0 ? "existing-job" : null,
                    SubmittedAt = i == 0 ? DateTimeOffset.UtcNow.AddHours(-1) : null }).ToList()
            }) });
    }
    private void MakeDue(string id)
    {
        using var db = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + Path.Combine(dir, "runs.db")); db.Open();
        using var cmd = db.CreateCommand(); cmd.CommandText = "UPDATE agent_runs SET due=0 WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id); cmd.ExecuteNonQuery();
    }
    [Fact]
    public async Task Waiting_checks_record_each_message_without_duplicate_submissions()
    {
        var run = await WaitingRun();
        Assert.Null(await runs.ClaimAsync(TimeSpan.FromMinutes(1))); // Explicit opt-in is required.
        run = await Tick(run.Id);
        Assert.Equal("waiting", run.Status); Assert.Equal("original-question", run.Question!.Id);
        var clip = SceneVideoLoop.State(run).Clips[0];
        Assert.True(clip.LastCheckSucceeded); Assert.NotNull(clip.LastCheckedAt);
        Assert.Equal("running", clip.RendererStatus); Assert.Empty(renderer.Requests);
        var count = (await runs.EventsAsync(run.Id)).Count;
        Assert.Null(await runs.ClaimAsync(TimeSpan.FromMinutes(1), monitors: SceneVideoLoop.WaitMonitors));
        MakeDue(run.Id); run = await Tick(run.Id);
        var nextEvents = await runs.EventsAsync(run.Id);
        Assert.Equal(count + 2, nextEvents.Count); // Each real poll has one request and one reply.
        Assert.Equal(nextEvents[^2].Route!.ExchangeId, nextEvents[^1].Route!.ExchangeId);
        Assert.Equal("Director", nextEvents[^2].Route!.From);
        Assert.Equal("Director", nextEvents[^1].Route!.To);
        Assert.Equal(2, renderer.CheckCount); Assert.Empty(renderer.Requests);
    }
    [Fact]
    public async Task Verified_running_job_clears_obsolete_retry_question_without_resubmitting()
    {
        var run = await WaitingRun(kind: "retry");
        var key = SceneVideoLoop.RenderKey(run, SceneVideoLoop.State(run));
        run = await Tick(run.Id);
        Assert.Equal("running", run.Status); Assert.Null(run.Question); Assert.Equal(0, run.Failures);
        Assert.Equal(key, SceneVideoLoop.RenderKey(run, SceneVideoLoop.State(run)));
        Assert.NotNull(SceneVideoLoop.State(run).Clips[0].WaitExtendedAt);
        Assert.Contains("Tracking recovered", run.Summary); Assert.Empty(renderer.Requests);
        await Assert.ThrowsAsync<InvalidOperationException>(() => runs.ReplyAsync(run.Id, run.Revision,
            new("original-question", "retry", null)));
    }
    [Fact]
    public async Task Waiting_completion_survives_restart_and_requires_decision_before_next_clip()
    {
        var run = await WaitingRun(); renderer.PollStatus = "completed";
        var reopened = new SqliteAgentRunStore(Path.Combine(dir, "runs.db"));
        var lease = await reopened.ClaimAsync(TimeSpan.FromMinutes(1), monitors: SceneVideoLoop.WaitMonitors);
        Assert.NotNull(lease); Assert.True(await reopened.RenewAsync(lease, TimeSpan.FromMinutes(1)));
        await Loop().StepAsync(lease, default); run = (await reopened.GetAsync(run.Id))!;
        Assert.Equal("waiting", run.Status); Assert.Equal("continue", run.Question!.Kind);
        Assert.Equal("completed", SceneVideoLoop.State(run).Clips[0].Status);
        Assert.NotNull(SceneVideoLoop.State(run).Clips[0].VideoUrl); Assert.Equal(1, SceneVideoLoop.State(run).Current);
        Assert.Empty(renderer.Requests); MakeDue(run.Id);
        Assert.Null(await reopened.ClaimAsync(TimeSpan.FromMinutes(1), monitors: SceneVideoLoop.WaitMonitors));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reopened.ReplyAsync(run.Id, 0, new("original-question", "wait", null)));
        run = await reopened.ReplyAsync(run.Id, run.Revision, new(run.Question.Id, "continue", null));
        run = await Tick(run.Id); Assert.Equal("running", run.Status); Assert.Equal("prepare", run.Phase);
        Assert.Null(run.Question); Assert.Empty(renderer.Requests);
    }
    [Fact]
    public async Task Waiting_final_clip_completion_finishes_run_without_an_obsolete_question()
    {
        var run = await WaitingRun(clips: 1); renderer.PollStatus = "completed";
        run = await Tick(run.Id); Assert.Equal("completed", run.Status); Assert.Null(run.Question);
        Assert.Empty(renderer.Requests);
    }
    [Fact]
    public async Task Waiting_failure_requires_explicit_retry_and_network_failure_keeps_monitoring()
    {
        var run = await WaitingRun(); renderer.PollFailure = true;
        run = await Tick(run.Id); Assert.Equal("timeout", run.Question!.Kind);
        Assert.False(SceneVideoLoop.State(run).Clips[0].LastCheckSucceeded);
        Assert.Contains("retry automatically", run.Summary);
        renderer.PollFailure = false; renderer.PollStatus = "failed"; MakeDue(run.Id);
        run = await Tick(run.Id); Assert.Equal("render-failed", run.Question!.Kind);
        Assert.Equal(1, SceneVideoLoop.State(run).Clips[0].RenderAttempt); Assert.Empty(renderer.Requests);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Human_reply_or_cancel_invalidates_in_flight_observation(bool cancel)
    {
        var run = await WaitingRun();
        var lease = (await runs.ClaimAsync(TimeSpan.FromMinutes(1), monitors: SceneVideoLoop.WaitMonitors))!;
        if (cancel) await runs.CancelAsync(run.Id);
        else await runs.ReplyAsync(run.Id, run.Revision, new(run.Question!.Id, "wait", null));
        Assert.False(await runs.RenewAsync(lease, TimeSpan.FromMinutes(1)));
        await Assert.ThrowsAsync<OperationCanceledException>(() => Loop().StepAsync(lease, default));
        run = (await runs.GetAsync(run.Id))!; Assert.Equal(cancel ? "cancelled" : "queued", run.Status);
    }
    [Fact]
    public async Task Deadline_checks_completed_job_before_asking_to_wait_and_preserves_submission_time()
    {
        var run = await WaitingRun(waiting: false); renderer.PollStatus = "completed";
        run = await Tick(run.Id); Assert.Null(run.Question); Assert.Equal("prepare", run.Phase);
        Assert.Equal("completed", SceneVideoLoop.State(run).Clips[0].Status);
    }
    [Fact]
    public async Task Extending_wait_does_not_change_original_submission_time()
    {
        var run = await WaitingRun(); var submitted = SceneVideoLoop.State(run).Clips[0].SubmittedAt;
        run = await runs.ReplyAsync(run.Id, run.Revision, new(run.Question!.Id, "wait", null));
        run = await Tick(run.Id);
        Assert.Equal(submitted, SceneVideoLoop.State(run).Clips[0].SubmittedAt);
        Assert.NotNull(SceneVideoLoop.State(run).Clips[0].WaitExtendedAt);
    }
    [Fact]
    public async Task Progress_flows_through_polls_with_paired_message_routes()
    {
        options.CurrentValue.RenderTimeoutMinutes = 240;
        renderer.PollProgress = new(63, "Generating video", "Sampling frames", DateTimeOffset.UtcNow);
        var run = await WaitingRun(waiting: false);
        run = await Tick(run.Id);
        Assert.Contains("63%", run.Summary);
        Assert.Equal(63, SceneVideoLoop.State(run).Clips[0].Progress!.Percent);
        var count = (await runs.EventsAsync(run.Id)).Count;
        MakeDue(run.Id); run = await Tick(run.Id);
        var nextEvents = await runs.EventsAsync(run.Id);
        Assert.Equal(count + 2, nextEvents.Count); // Each real poll has one request and one reply.
        Assert.Equal(nextEvents[^2].Route!.ExchangeId, nextEvents[^1].Route!.ExchangeId);
        Assert.Equal("Director", nextEvents[^2].Route!.From);
        Assert.Equal("Director", nextEvents[^1].Route!.To);
        options.CurrentValue.RenderTimeoutMinutes = 1;
        MakeDue(run.Id); run = await Tick(run.Id); Assert.Equal("waiting", run.Status);
        renderer.PollProgress = new(100, "Processing video", "Encoding", DateTimeOffset.UtcNow);
        MakeDue(run.Id); run = await Tick(run.Id);
        Assert.Equal(99, SceneVideoLoop.State(run).Clips[0].Progress!.Percent);
        Assert.Contains("99%", run.Summary);
        renderer.PollFailure = true; MakeDue(run.Id); run = await Tick(run.Id);
        Assert.False(SceneVideoLoop.State(run).Clips[0].LastCheckSucceeded);
        Assert.Equal(99, SceneVideoLoop.State(run).Clips[0].Progress!.Percent);
        renderer.PollFailure = false; renderer.PollStatus = "completed";
        MakeDue(run.Id); run = await Tick(run.Id);
        Assert.Equal(100, SceneVideoLoop.State(run).Clips[0].Progress!.Percent);
        Assert.False(SceneVideoLoop.State(run).Clips[0].Progress!.Estimated);
        Assert.Equal("continue", run.Question!.Kind); Assert.Empty(renderer.Requests);
    }
    [Fact]
    public async Task Planning_questions_are_never_claimed_for_render_monitoring()
    {
        var run = await Tick((await Create(review: true)).Id);
        Assert.Equal("review", run.Question!.Kind);
        Assert.Null(await runs.ClaimAsync(TimeSpan.FromMinutes(1), monitors: SceneVideoLoop.WaitMonitors));
        Assert.Equal(0, renderer.CheckCount);
    }
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { Directory.Delete(dir, true); } catch (IOException) { } }
    private readonly Production production = new();
    private readonly Critic critic = new();
    private readonly Audio audio = new();
    private async Task<AgentRun> ProductionRun(bool review = true)
    {
        var run = await Create(review, seconds: 30);
        var lease = (await runs.ClaimAsync(TimeSpan.FromMinutes(1)))!;
        var state = SceneVideoLoop.State(lease.Run); state.ProductionEnabled = true;
        state.StoryId = "story"; state.StoryName = "Traveller"; state.Input = state.Input with { StoryMode = "continue" };
        lease.Run.Phase = "project"; lease.Run.State = JsonSerializer.SerializeToElement(state);
        await runs.SaveAsync(lease, "test", "ready", "Ready"); return (await runs.GetAsync(run.Id))!;
    }
    private async Task<AgentRun> ActorTick(string id, bool delaySavedReply = false)
    {
        using var runtime = new InMemoryActorRuntime(); await runtime.InitializeAsync(new Dictionary<string, object>());
        // Reopen the durable store each step to exercise restart boundaries.
        IAgentRunStore reopened = new SqliteAgentRunStore(Path.Combine(dir, "runs.db"));
        if (delaySavedReply) {
            var proxy = System.Reflection.DispatchProxy.Create<IAgentRunStore, DelayedSaveStore>();
            ((DelayedSaveStore)(object)proxy).Inner = reopened;
            reopened = proxy;
        }
        var loop = new SceneVideoLoop(reopened, memory, planner, interpreter, renderer, options, NullLogger<SceneVideoLoop>.Instance, production: production, critic: critic, audio: audio);
        var worker = new SceneVideoWorker(reopened, loop, runtime, options, NullLogger<SceneVideoWorker>.Instance);
        var lease = await reopened.ClaimAsync(TimeSpan.FromMinutes(1), monitors: SceneVideoLoop.WaitMonitors);
        Assert.NotNull(lease);
        await worker.ExecuteLeaseAsync(lease, default);
        var saved = (await reopened.GetAsync(id))!;
        var active = (await runtime.GetActiveActorIdsAsync()).ToList();
        if (saved.IsTerminal)
            Assert.Empty(active);
        else
            foreach (var role in SceneVideoRoles.Agents)
                Assert.Contains(SceneVideoWorker.ActorId(id, role), active);
        await runtime.ShutdownAsync();
        return saved;
    }
    [Fact]
    public async Task Saved_checkpoint_does_not_cancel_its_pending_actor_reply()
    {
        var run = await Create(review: true);
        var saved = await ActorTick(run.Id, delaySavedReply: true);
        Assert.Equal("waiting", saved.Status);
        Assert.Equal("review", saved.Question!.Kind);
        Assert.True(saved.Revision > run.Revision);
    }
    public class DelayedSaveStore : System.Reflection.DispatchProxy
    {
        public IAgentRunStore Inner = null!;
        private readonly TaskCompletionSource renewed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? args)
        {
            var result = method!.Invoke(Inner, args);
            if (method.Name == nameof(IAgentRunStore.RenewAsync)) renewed.TrySetResult();
            if (method.Name == nameof(IAgentRunStore.SaveAsync)) return DelayReply((Task)result!, (CancellationToken)args![4]!);
            return result;
        }
        private async Task DelayReply(Task save, CancellationToken ct)
        {
            await save;
            await renewed.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await Task.Yield();
            ct.ThrowIfCancellationRequested();
        }
    }
    [Fact]
    public async Task Production_actors_dry_run_plan_preserve_identity_and_deliver_only_after_review()
    {
        var run = await ProductionRun();
        for (var i = 0; i < 10 && run.Status != "waiting"; i++) run = await ActorTick(run.Id);
        Assert.Equal("review", run.Question!.Kind); Assert.Empty(renderer.Requests);
        Assert.Empty((await memory.GetAsync(run.WorkspaceId)).Prompts ?? []);
        Assert.All(SceneVideoLoop.State(run).Clips, c => Assert.Contains(c.PlanDetails!.Participants, e => e.Name == "Traveller"));
        await runs.ReplyAsync(run.Id, run.Revision, new(run.Question.Id, "approve", null));
        for (var i = 0; i < 20 && !run.IsTerminal; i++) run = await ActorTick(run.Id);
        Assert.Equal("completed", run.Status);
        Assert.Equal("https://cynelo.test/finished.mp4", SceneVideoLoop.State(run).FinalVideoUrl);
        Assert.All(renderer.Requests, r => Assert.Equal("project", r.ProjectId));
        Assert.Single((await memory.GetAsync(run.WorkspaceId)).Entities);
        Assert.Equal(2, critic.Calls); Assert.Single(production.Deliveries);
        Assert.Equal(new[] { $"{run.Id}-1-1", $"{run.Id}-2-1" }, production.Deliveries[0]);
        var events = await runs.EventsAsync(run.Id);
        Assert.Contains(events, e => e.Agent == "Project agent");
        Assert.Contains(events, e => e.Agent == "Planner");
        Assert.Contains(events, e => e.Agent == "Scene Memory agent");
        Assert.Contains(events, e => e.Agent == "Render agent");
        Assert.Contains(events, e => e.Agent == "Visual critic");
        Assert.Contains(events, e => e.Agent == "Editor agent" && e.Kind == "completed");
        // No Director/Audio Engineer hat-switching onto other role labels as the executing agent for their phases.
        Assert.DoesNotContain(events, e => e.Agent == "Director" && e.Route is { From: "Director", To: "Video rendering" or "Film assembly" or "Project storage" });
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Planned_relationship_values_rebase_after_first_clip_and_saved_retry(bool savedRetry)
    {
        interpreter.RelationshipInValue = true;
        var run = await ProductionRun(false);
        for (var i = 0; i < 20 && !(run.Phase == "prepare" && SceneVideoLoop.State(run).Current == 1); i++)
            run = await ActorTick(run.Id);
        Assert.Equal("prepare", run.Phase);
        var state = SceneVideoLoop.State(run);
        Assert.Equal(1, state.Current);
        var plannedId = state.PlannedEntities.Single(e => e.Name == "Traveller").Id;
        var actualId = (await memory.GetAsync(run.WorkspaceId)).Entities.Single().Id;
        Assert.NotEqual(plannedId, actualId);
        var completedClip = JsonSerializer.Serialize(state.Clips[0]);
        var originalPreview = state.Clips[1].Preview!;
        Assert.Equal(plannedId, originalPreview.Extraction.Changes.Single(c => c.Property == "held_by").Value);
        if (savedRetry)
        {
            var lease = (await runs.ClaimAsync(TimeSpan.FromMinutes(1)))!;
            lease.Run.Status = "waiting"; lease.Run.Failures = 3;
            lease.Run.Question = new("saved-question", "retry", "Retry this step", [new("retry", "Retry this step")]);
            await runs.SaveAsync(lease, "test", "waiting", "Saved failed preparation");
            run = (await runs.GetAsync(run.Id))!;
            await runs.ReplyAsync(run.Id, run.Revision, new(run.Question!.Id, "retry", null));
            run = await ActorTick(run.Id);
        }
        run = await ActorTick(run.Id);
        Assert.Equal("commit", run.Phase);
        state = SceneVideoLoop.State(run);
        Assert.Equal(completedClip, JsonSerializer.Serialize(state.Clips[0]));
        Assert.Equal(actualId, state.Clips[1].Preview!.Extraction.Changes.Single(c => c.Property == "held_by").Value);
        Assert.Equal(plannedId, state.Clips[1].Preview!.Extraction.Changes.Single(c => c.Property == "note").Value);
        Assert.Single(renderer.Requests);
        for (var i = 0; i < 12 && !run.IsTerminal; i++) run = await ActorTick(run.Id);
        Assert.Equal("completed", run.Status);
        Assert.Equal(2, renderer.Requests.Count);
        var workspace = await memory.GetAsync(run.WorkspaceId);
        var can = workspace.Entities.Single(e => e.Name == "Watering can");
        Assert.Contains(workspace.History, f => f.EntityId == can.Id && f.Property == "held_by" && f.RelatedEntityId == actualId);
        Assert.Equal(2, workspace.Prompts!.Count);
    }
    [Fact]
    public async Task Observed_ending_is_durable_and_guides_next_render_without_changing_story_facts()
    {
        var run = await ProductionRun(false);
        for (var i = 0; i < 20 && !(run.Phase == "prepare" && SceneVideoLoop.State(run).Current == 1); i++) run = await ActorTick(run.Id);
        var state = SceneVideoLoop.State(run);
        Assert.Equal(1, state.Current);
        var workspace = await memory.GetAsync(run.WorkspaceId);
        Assert.Equal(state.WorkspaceRevision, ScenePromptReducer.Revision(workspace));
        var observation = Assert.Single(await memory.ReadBoundariesAsync(run.WorkspaceId, state.Clips[0].ShotId));
        Assert.Equal("Entered greenhouse", observation.Ending.CompletedActions);
        Assert.Equal($"{run.Id}-1-1", observation.RenderKey);
        for (var i = 0; i < 12 && !run.IsTerminal; i++) run = await ActorTick(run.Id);
        Assert.Equal("completed", run.Status);
        Assert.Null(renderer.Requests[0].BoundaryInstruction);
        Assert.Contains("Entered greenhouse", renderer.Requests[1].BoundaryInstruction);
        Assert.Contains("Do not replay completed", renderer.Requests[1].BoundaryInstruction);
        Assert.DoesNotContain("OBSERVED MOVEMENT", renderer.Requests[1].Prompt);
    }
    [Theory]
    [InlineData("recheck")]
    [InlineData("continue")]
    public async Task Uncertain_ending_waits_for_explicit_choice_without_rendering_next_clip(string choice)
    {
        critic.EndingStatus = "uncertain";
        var run = await ProductionRun(false);
        for (var i = 0; i < 20 && run.Status != "waiting"; i++) run = await ActorTick(run.Id);
        Assert.Equal("boundary", run.Question!.Kind); Assert.Single(renderer.Requests);
        Assert.False(SceneVideoLoop.State(run).Clips[1].BoundaryBypass);
        critic.EndingStatus = "observed";
        await runs.ReplyAsync(run.Id, run.Revision, new(run.Question.Id, choice, null));
        for (var i = 0; i < 16 && !run.IsTerminal; i++) run = await ActorTick(run.Id);
        Assert.Equal("completed", run.Status); Assert.Equal(2, renderer.Requests.Count);
        Assert.Equal(choice == "continue", SceneVideoLoop.State(run).Clips[1].BoundaryBypass);
        Assert.Contains(choice == "continue" ? "unverified" : "OBSERVED MOVEMENT", renderer.Requests[1].BoundaryInstruction);
    }
    [Fact]
    public async Task Inconsistent_motion_still_asks_and_does_not_submit_the_next_clip()
    {
        production.EndingMotion = new(0.4, "mixed", false);
        critic.EndingStatus = "observed";
        var run = await ProductionRun(false);
        for (var i = 0; i < 20 && run.Status != "waiting"; i++) run = await ActorTick(run.Id);
        Assert.Equal("boundary", run.Question!.Kind);
        Assert.Single(renderer.Requests);
        Assert.Equal("uncertain", SceneVideoLoop.State(run).Clips[0].Boundary!.Ending.Status);
    }
    [Fact]
    public async Task Recheck_ignores_typed_words_and_does_not_render()
    {
        critic.EndingStatus = "uncertain";
        var run = await ProductionRun(false);
        for (var i = 0; i < 20 && run.Status != "waiting"; i++) run = await ActorTick(run.Id);
        Assert.Equal("boundary", run.Question!.Kind);
        await runs.ReplyAsync(run.Id, run.Revision, new(run.Question.Id, "recheck", "use the last frame"));
        run = await ActorTick(run.Id);
        Assert.DoesNotContain("use the last frame", critic.LastPrompt);
        Assert.DoesNotContain(SceneVideoLoop.State(run).HumanNotes, n => n.Contains("last frame"));
        Assert.Single(renderer.Requests);
        Assert.NotEqual("observed", SceneVideoLoop.State(run).Clips[0].Boundary!.Ending.Status);
    }
    [Fact]
    public async Task Continue_ignores_typed_words_and_leaves_the_ending_unverified()
    {
        critic.EndingStatus = "uncertain";
        var run = await ProductionRun(false);
        for (var i = 0; i < 20 && run.Status != "waiting"; i++) run = await ActorTick(run.Id);
        var ending = SceneVideoLoop.State(run).Clips[0].Boundary!.Ending.Status;
        Assert.Contains(run.Question!.Choices, c => c.Id == "prompt");
        await runs.ReplyAsync(run.Id, run.Revision, new(run.Question.Id, "continue", "smoothly use the last frame"));
        run = await ActorTick(run.Id);
        for (var i = 0; i < 12 && renderer.Requests.Count < 2 && run.Status != "waiting"; i++) run = await ActorTick(run.Id);
        var state = SceneVideoLoop.State(run);
        Assert.True(state.Clips[1].BoundaryBypass);
        Assert.Null(state.Clips[1].BoundaryNote);
        Assert.DoesNotContain(state.HumanNotes, n => n.Contains("last frame"));
        Assert.Contains("unverified", renderer.Requests[1].BoundaryInstruction);
        Assert.DoesNotContain("smoothly use the last frame", renderer.Requests[1].BoundaryInstruction);
        Assert.DoesNotContain("OBSERVED MOVEMENT", renderer.Requests[1].BoundaryInstruction);
        Assert.Equal(ending, state.Clips[0].Boundary!.Ending.Status);
    }
    [Fact]
    public async Task Transition_prompt_guides_the_next_clip_without_verifying_the_ending()
    {
        critic.EndingStatus = "uncertain";
        var run = await ProductionRun(false);
        for (var i = 0; i < 20 && run.Status != "waiting"; i++) run = await ActorTick(run.Id);
        var ending = SceneVideoLoop.State(run).Clips[0].Boundary!.Ending.Status;
        await runs.ReplyAsync(run.Id, run.Revision, new(run.Question!.Id, "prompt", "  smoothly use the last frame  "));
        run = await ActorTick(run.Id);
        for (var i = 0; i < 12 && renderer.Requests.Count < 2 && run.Status != "waiting"; i++) run = await ActorTick(run.Id);
        var state = SceneVideoLoop.State(run);
        Assert.True(state.Clips[1].BoundaryBypass);
        Assert.Equal("smoothly use the last frame", state.Clips[1].BoundaryNote);
        Assert.Contains("unverified", renderer.Requests[1].BoundaryInstruction);
        Assert.Contains("Creator transition prompt: smoothly use the last frame", renderer.Requests[1].BoundaryInstruction);
        Assert.Contains("last frame and scene memory", renderer.Requests[1].BoundaryInstruction);
        Assert.DoesNotContain("OBSERVED MOVEMENT", renderer.Requests[1].BoundaryInstruction);
        Assert.Equal(ending, state.Clips[0].Boundary!.Ending.Status);
    }
    [Fact]
    public async Task Empty_transition_prompt_stays_waiting()
    {
        critic.EndingStatus = "uncertain";
        var run = await ProductionRun(false);
        for (var i = 0; i < 20 && run.Status != "waiting"; i++) run = await ActorTick(run.Id);
        await runs.ReplyAsync(run.Id, run.Revision, new(run.Question!.Id, "prompt", "   "));
        run = await ActorTick(run.Id);
        Assert.Equal("waiting", run.Status);
        Assert.Equal("boundary", run.Question!.Kind);
        Assert.False(SceneVideoLoop.State(run).Clips[1].BoundaryBypass);
        Assert.Null(SceneVideoLoop.State(run).Clips[1].BoundaryNote);
        Assert.Single(renderer.Requests);
    }
    [Fact]
    public async Task Boundary_words_without_a_choice_are_not_applied()
    {
        critic.EndingStatus = "uncertain";
        var run = await ProductionRun(false);
        for (var i = 0; i < 20 && run.Status != "waiting"; i++) run = await ActorTick(run.Id);
        await runs.ReplyAsync(run.Id, run.Revision, new(run.Question!.Id, "", "use the last frame"));
        run = await ActorTick(run.Id);
        Assert.Equal("waiting", run.Status);
        Assert.Equal("boundary", run.Question!.Kind);
        Assert.False(SceneVideoLoop.State(run).Clips[1].BoundaryBypass);
        Assert.Null(SceneVideoLoop.State(run).Clips[1].BoundaryNote);
        Assert.DoesNotContain(SceneVideoLoop.State(run).HumanNotes, n => n.Contains("last frame"));
        Assert.Single(renderer.Requests);
    }
    [Fact]
    public async Task Stop_ignores_typed_words()
    {
        critic.EndingStatus = "uncertain";
        var run = await ProductionRun(false);
        for (var i = 0; i < 20 && run.Status != "waiting"; i++) run = await ActorTick(run.Id);
        await runs.ReplyAsync(run.Id, run.Revision, new(run.Question!.Id, "cancel", "use the last frame"));
        run = await ActorTick(run.Id);
        Assert.Equal("cancelled", run.Status);
        Assert.DoesNotContain("last frame", run.Summary);
        Assert.DoesNotContain(SceneVideoLoop.State(run).HumanNotes, n => n.Contains("last frame"));
        Assert.Single(renderer.Requests);
    }
    [Fact]
    public async Task Measured_speech_saves_confirmed_dialogue_and_leaves_planned_events_planned()
    {
        production.Speech = new("detected", "en", .98, "Hello my friend", "English speech", Words:
            [new SceneSpeechWord("Hello", 4, 5), new SceneSpeechWord("friend", 8, 9)]);
        var run = await SpeechRun();
        for (var i = 0; i < 40 && !run.IsTerminal; i++) run = await ActorTick(run.Id);
        Assert.Equal("completed", run.Status);
        var state = SceneVideoLoop.State(run);
        var shot = (await memory.GetAsync(run.WorkspaceId)).Prompts!.Single(p => p.Id == state.Clips[0].ShotId);
        var spoken = Assert.Single(shot.TimedEvents ?? [], e => e.Kind == "dialogue" && e.Status == "confirmed");
        Assert.Equal($"{run.Id}-1-1", spoken.TakeId);
        Assert.Equal("Hello my friend", spoken.Dialogue);
        Assert.Equal(4000, spoken.Timing.Start);
        Assert.Equal(9000, spoken.Timing.End);
        Assert.All(shot.TimedEvents!.Where(e => e.Id != spoken.Id), e => Assert.Equal("planned", e.Status));
        Assert.Equal(ScenePromptReducer.Revision(await memory.GetAsync(run.WorkspaceId)), state.WorkspaceRevision);
    }
    [Fact]
    public async Task Transition_concerns_require_review_and_only_replace_current_take_on_request()
    {
        var run = await ProductionRun(false);
        for (var i = 0; i < 20 && !(run.Phase == "prepare" && SceneVideoLoop.State(run).Current == 1); i++) run = await ActorTick(run.Id);
        var original = JsonSerializer.Serialize(SceneVideoLoop.State(run).Clips[0]);
        critic.Status = "concerns";
        for (var i = 0; i < 8 && run.Status != "waiting"; i++) run = await ActorTick(run.Id);
        Assert.Equal("continuity", run.Question!.Kind); Assert.Equal(2, renderer.Requests.Count);
        Assert.Contains(run.Question.Choices, c => c.Id == "regenerate");
        await runs.ReplyAsync(run.Id, run.Revision, new(run.Question.Id, "recheck", null));
        run = await ActorTick(run.Id); run = await ActorTick(run.Id);
        Assert.Equal("waiting", run.Status); Assert.Equal(2, renderer.Requests.Count);
        await runs.ReplyAsync(run.Id, run.Revision, new(run.Question!.Id, "regenerate", null));
        run = await ActorTick(run.Id);
        Assert.Null(SceneVideoLoop.State(run).Clips[1].Boundary);
        critic.Status = "passed";
        for (var i = 0; i < 12 && !run.IsTerminal; i++) run = await ActorTick(run.Id);
        Assert.Equal("completed", run.Status); Assert.Equal(3, renderer.Requests.Count);
        Assert.Equal($"{run.Id}-2-1", renderer.Requests[2].ReplacesJobKey);
        Assert.Equal($"{run.Id}-1-1", renderer.Requests[2].PreviousJobKey);
        Assert.Equal(original, JsonSerializer.Serialize(SceneVideoLoop.State(run).Clips[0]));
        Assert.Equal(2, (await memory.ReadBoundariesAsync(run.WorkspaceId, $"{run.Id}-2")).Count);
    }
    [Fact]
    public async Task Visual_replacement_preserves_earlier_approved_audio_and_requires_new_audio_review()
    {
        var run = await ProductionRun(false);
        var lease = (await runs.ClaimAsync(TimeSpan.FromMinutes(1)))!;
        var state = SceneVideoLoop.State(lease.Run); state.AudioEnabled = true;
        lease.Run.State = JsonSerializer.SerializeToElement(state);
        await runs.SaveAsync(lease, "test", "ready", "Audio enabled");
        for (var i = 0; i < 25 && run.Status != "waiting"; i++) run = await ActorTick(run.Id);
        Assert.Equal("clip-audio-review", run.Question!.Kind);
        await runs.ReplyAsync(run.Id, run.Revision, new(run.Question.Id, "approve", null));
        run = await ActorTick(run.Id);
        var approved = JsonSerializer.Serialize(SceneVideoLoop.State(run).Clips[0]);
        critic.Status = "concerns";
        for (var i = 0; i < 12 && run.Status != "waiting"; i++) run = await ActorTick(run.Id);
        Assert.Equal("continuity", run.Question!.Kind);
        await runs.ReplyAsync(run.Id, run.Revision, new(run.Question.Id, "regenerate", null));
        run = await ActorTick(run.Id); critic.Status = "passed";
        for (var i = 0; i < 12 && run.Status != "waiting"; i++) run = await ActorTick(run.Id);
        Assert.Equal("clip-audio-review", run.Question!.Kind);
        state = SceneVideoLoop.State(run);
        Assert.Equal(approved, JsonSerializer.Serialize(state.Clips[0]));
        Assert.Equal("version-1", state.Clips[0].ApprovedAudioVersion);
        Assert.Null(state.Clips[1].ApprovedAudioVersion);
        Assert.Equal(3, renderer.Requests.Count); Assert.Empty(production.Deliveries);
        Assert.Single(production.AudioPlan!.Approved!);
    }
    [Fact]
    public async Task Planning_progress_is_persisted_once_without_publishing_model_text()
    {
        var run = await ProductionRun();
        for (var i = 0; i < 3; i++) run = await ActorTick(run.Id);
        var events = await runs.EventsAsync(run.Id);
        Assert.Contains(events, e => e.Agent == "Planner" && e.Message.Contains("1/2 sections checked"));
        Assert.Single(events.Where(e => e.Message.Contains("Checking the final scene relationships")));
        Assert.DoesNotContain(events, e => e.Message.Contains("private model text"));
        Assert.Empty(renderer.Requests);
    }
    [Fact]
    public async Task Legacy_waiting_planner_gets_recovery_choices_without_starting_model_work()
    {
        var run = await ProductionRun();
        for (var i = 0; i < 3; i++) run = await ActorTick(run.Id);
        var lease = (await runs.ClaimAsync(TimeSpan.FromMinutes(1)))!;
        lease.Run.Status = "waiting";
        lease.Run.Question = new("old", "retry", "Retry this step", [new("retry", "Retry this step")]);
        await runs.SaveAsync(lease, "test", "waiting", "Old question");
        var calls = interpreter.HistorySizes.Count;
        run = await ActorTick(run.Id);
        Assert.Equal("planning-retry", run.Question!.Kind);
        Assert.Contains(run.Question.Choices, c => c.Id == "reset-plan");
        Assert.Equal(calls, interpreter.HistorySizes.Count); Assert.Equal(0, interpreter.Resets);
        Assert.Null(await runs.ClaimAsync(TimeSpan.FromMinutes(1), monitors: SceneVideoLoop.WaitMonitors));
    }
    [Fact]
    public async Task Explicit_planning_reset_restarts_only_failed_shot()
    {
        var run = await ProductionRun();
        for (var i = 0; i < 3; i++) run = await ActorTick(run.Id);
        var first = JsonSerializer.Serialize(SceneVideoLoop.State(run).Clips[0]);
        interpreter.NextError = new ScenePromptInterpretationException("invalid", "fake", "prompt",
            [new(3, [ScenePromptDiagnostics.Issue("prompt", "invalid_plan", "$", "invalid")])]);
        run = await ActorTick(run.Id);
        await runs.ReplyAsync(run.Id, run.Revision, new(run.Question!.Id, "reset-plan", null));
        run = await ActorTick(run.Id); run = await ActorTick(run.Id);
        Assert.Equal("review", run.Question!.Kind); Assert.Equal(1, interpreter.Resets);
        Assert.Equal(first, JsonSerializer.Serialize(SceneVideoLoop.State(run).Clips[0]));
        Assert.Empty(renderer.Requests);
    }
    [Fact]
    public async Task Planning_deadline_saves_actionable_wait_before_actor_timeout()
    {
        var run = await ProductionRun();
        for (var i = 0; i < 3; i++) run = await ActorTick(run.Id);
        options.CurrentValue.PlanCheckTimeoutSeconds = .05;
        interpreter.WaitForCancellation = true;
        run = await ActorTick(run.Id);
        Assert.Equal("waiting", run.Status); Assert.Equal("plan-review", run.Phase);
        Assert.Contains("time limit", run.Question!.Prompt);
        Assert.Equal(1, SceneVideoLoop.State(run).ReviewIndex); Assert.Empty(renderer.Requests);
    }
    [Fact]
    public async Task Exhausted_semantic_repairs_pause_once_and_allow_revising_only_failed_shot()
    {
        var run = await ProductionRun();
        for (var i = 0; i < 3; i++) run = await ActorTick(run.Id);
        var first = JsonSerializer.Serialize(SceneVideoLoop.State(run).Clips[0]);
        interpreter.NextError = new ScenePromptInterpretationException("secret provider body", "fake", "prompt",
            [new(3, [ScenePromptDiagnostics.Issue("prompt", "invalid_change", "continuity.changes[0]", "secret")])]);
        run = await ActorTick(run.Id);
        Assert.Equal("waiting", run.Status); Assert.Equal(1, run.Failures);
        var problem = SceneVideoLoop.State(run).PlanProblem!;
        Assert.Equal(2, problem.ClipNumber); Assert.Equal("Final continuity check", problem.Stage);
        Assert.DoesNotContain("secret", JsonSerializer.Serialize(problem));
        Assert.Contains(run.Question!.Choices, c => c.Id == "revise-shot");
        Assert.Null(await runs.ClaimAsync(TimeSpan.FromMinutes(1), monitors: SceneVideoLoop.WaitMonitors));
        await runs.ReplyAsync(run.Id, run.Revision, new(run.Question.Id, "revise-shot", null));
        run = await ActorTick(run.Id);
        Assert.Equal("waiting", run.Status); Assert.Equal(0, interpreter.Resets);
        await runs.ReplyAsync(run.Id, run.Revision, new(run.Question!.Id, "revise-shot", "The traveller places the parcel on the counter."));
        run = await ActorTick(run.Id); run = await ActorTick(run.Id);
        Assert.Equal("review", run.Question!.Kind); Assert.Equal(1, interpreter.Resets);
        Assert.Equal(first, JsonSerializer.Serialize(SceneVideoLoop.State(run).Clips[0]));
        Assert.Equal("The traveller places the parcel on the counter.", SceneVideoLoop.State(run).Clips[1].Prompt);
        Assert.Null(SceneVideoLoop.State(run).PlanProblem); Assert.Empty(renderer.Requests);
    }
    [Theory]
    [InlineData("invalid_participants", "valid participant")]
    [InlineData("invalid_plan", "inconsistent plan")]
    [InlineData("section_timeout", "took too long")]
    public async Task Failed_review_preserves_checked_clips_and_restarts_at_failed_clip(string code, string message)
    {
        var run = await ProductionRun();
        for (var i = 0; i < 3; i++) run = await ActorTick(run.Id); // project, plan, first clip review
        var state = SceneVideoLoop.State(run);
        Assert.Equal(1, state.ReviewIndex);
        var checkedClip = JsonSerializer.Serialize(state.Clips[0]);
        var lease = (await runs.ClaimAsync(TimeSpan.FromMinutes(1)))!;
        state.MaxAttempts = 1;
        lease.Run.State = JsonSerializer.SerializeToElement(state);
        await runs.SaveAsync(lease, "test", "ready", "Ready");
        interpreter.NextError = new ScenePromptInterpretationException("private upstream information", "fake", "prompt",
            [new(3, [ScenePromptDiagnostics.Issue("prompt", code, "events[0]", "private upstream information")])]);
        run = await ActorTick(run.Id);
        Assert.Equal("waiting", run.Status);
        Assert.Contains(message, run.Question!.Prompt);
        Assert.DoesNotContain("connection", run.Question.Prompt);
        Assert.DoesNotContain("private upstream", run.Question.Prompt);
        Assert.Equal(1, SceneVideoLoop.State(run).ReviewIndex);
        Assert.Equal(checkedClip, JsonSerializer.Serialize(SceneVideoLoop.State(run).Clips[0]));
        await runs.ReplyAsync(run.Id, run.Revision, new(run.Question.Id, "retry", null));
        run = await ActorTick(run.Id); // reply
        run = await ActorTick(run.Id); // only second clip is interpreted
        Assert.Equal("review", run.Question!.Kind);
        Assert.Equal(0, interpreter.Resets);
        Assert.Equal(2, interpreter.HistorySizes.Count);
        Assert.Equal(checkedClip, JsonSerializer.Serialize(SceneVideoLoop.State(run).Clips[0]));
        Assert.Empty((await memory.GetAsync(run.WorkspaceId)).Entities);
        Assert.Empty(renderer.Requests);
    }

    [Theory]
    [InlineData("concerns")]
    [InlineData("unavailable")]
    [InlineData("uncertain")]
    public async Task Visual_concerns_pause_without_rerender_and_explicit_acceptance_resumes(string status)
    {
        critic.Status = status;
        var run = await ProductionRun(review: false);
        for (var i = 0; i < 15 && run.Status != "waiting"; i++) run = await ActorTick(run.Id);
        Assert.Equal("continuity", run.Question!.Kind); Assert.Single(renderer.Requests); Assert.Empty(production.Deliveries);
        var question = run.Question.Id;
        await runs.ReplyAsync(run.Id, run.Revision, new(question, null, "I watched it"));
        run = await ActorTick(run.Id); Assert.Equal("waiting", run.Status); Assert.Single(renderer.Requests);
        await Assert.ThrowsAsync<InvalidOperationException>(() => runs.ReplyAsync(run.Id, run.Revision, new(question, "accept", null)));
        await runs.ReplyAsync(run.Id, run.Revision, new(run.Question!.Id, "accept", null));
        critic.Status = "passed";
        for (var i = 0; i < 15 && !run.IsTerminal; i++) run = await ActorTick(run.Id);
        Assert.Equal("completed", run.Status); Assert.Equal(2, renderer.Requests.Count); Assert.Single(production.Deliveries);
    }
    [Fact]
    public async Task Cancelling_visual_review_preserves_clip_and_never_assembles_or_renders_next()
    {
        critic.Status = "concerns";
        var run = await ProductionRun(false);
        for (var i = 0; i < 15 && run.Status != "waiting"; i++) run = await ActorTick(run.Id);
        await runs.ReplyAsync(run.Id, run.Revision, new(run.Question!.Id, "cancel", null));
        run = await ActorTick(run.Id);
        Assert.Equal("cancelled", run.Status); Assert.NotNull(SceneVideoLoop.State(run).Clips[0].VideoUrl);
        Assert.Single(renderer.Requests); Assert.Empty(production.Deliveries);
    }
    [Theory]
    [InlineData("improved", true)]
    [InlineData("original", true)]
    [InlineData("original", false)]
    public async Task Audio_engineer_and_director_checkpoint_report_and_wait_for_explicit_choice(string choice, bool available)
    {
        var run = await ProductionRun(false);
        var lease = (await runs.ClaimAsync(TimeSpan.FromMinutes(1)))!;
        var state = SceneVideoLoop.State(lease.Run); state.AudioEnabled = true;
        lease.Run.State = JsonSerializer.SerializeToElement(state);
        await runs.SaveAsync(lease, "test", "ready", "Ready");
        production.AudioAvailable = available;
        run = await ReachFinalAudioReview(run);
        Assert.Equal("audio-review", run.Question!.Kind);
        Assert.Equal(available, run.Question.Choices.Any(c => c.Id == "improved"));
        Assert.Empty(production.Deliveries);
        await runs.ReplyAsync(run.Id, run.Revision, new(run.Question.Id, null, "Please keep my dialogue"));
        run = await ActorTick(run.Id);
        Assert.Equal("waiting", run.Status); Assert.Empty(production.Deliveries);
        await runs.ReplyAsync(run.Id, run.Revision, new(run.Question!.Id, "retry", null));
        for (var i = 0; i < 4; i++) run = await ActorTick(run.Id);
        Assert.Equal("audio-review", run.Question!.Kind);
        Assert.Equal(2, production.AudioCalls); Assert.Equal(2, renderer.Requests.Count);
        await runs.ReplyAsync(run.Id, run.Revision, new(run.Question.Id, choice, null));
        run = await ActorTick(run.Id); run = await ActorTick(run.Id);
        Assert.Equal("completed", run.Status); Assert.Equal(choice, production.AudioChoice);
        Assert.Single(production.Deliveries);
        var events = await runs.EventsAsync(run.Id);
        Assert.Contains(events, e=>e.Agent == "Audio Engineer");
        Assert.Contains(events, e=>e.Agent == "Director" && e.Kind == "waiting");
        // Audio phases: Director ↔ Audio Engineer only; Director never calls audio tools.
        var audioRoutes = events.Where(e => e.Route != null && e.Route.Phase is "audio-plan" or "audio-process" or "audio-review").Select(e => e.Route!).ToList();
        Assert.NotEmpty(audioRoutes);
        Assert.All(audioRoutes, r => Assert.True(
            (r.From == "Director" && r.To == "Audio Engineer") ||
            (r.From == "Audio Engineer" && r.To == "Director") ||
            (r.FromKind == "tool" && r.From is "Audio production" or "Speech inspection") ||
            (r.ToKind == "tool" && r.To is "Audio production" or "Speech inspection") ||
            (r.From == "Audio Engineer" && r.ToKind is "llm" or "tool") ||
            (r.To == "Audio Engineer" && r.FromKind is "llm" or "tool"),
            r.From + " → " + r.To));
        Assert.DoesNotContain(events, e => e.Route is { From: "Director", To: "Audio production" or "Speech inspection" });
    }
    private async Task<AgentRun> SpeechRun(string expected = "en")
    {
        var run = await ProductionRun(false);
        var lease = (await runs.ClaimAsync(TimeSpan.FromMinutes(1)))!;
        var state = SceneVideoLoop.State(lease.Run); state.SpeechReviewEnabled = true;
        state.ExpectedSpeechLanguage = expected;
        lease.Run.State = JsonSerializer.SerializeToElement(state);
        await runs.SaveAsync(lease,"test","ready","Speech enabled");
        return (await runs.GetAsync(run.Id))!;
    }
    [Fact]
    public async Task Native_H3_tags_reach_renderer_and_matching_language_does_not_hide_changed_words()
    {
        planner.Spoken = "The traveller says: \"Do not cross the bridge.\"";
        production.Speech = new("detected","en",.99,"Do cross the bridge.","English speech");
        var run = await SpeechRun();
        for(var i=0;i<25 && run.Status!="waiting";i++) run=await ActorTick(run.Id);
        Assert.Equal("speech-review",run.Question!.Kind);
        Assert.Contains("do not match",run.Question.Prompt);
        var speech = SceneVideoLoop.State(run).Clips[0].Speech!;
        Assert.Equal("Do cross the bridge.", speech.Transcript);
        Assert.Contains(speech.Issues!, issue => issue.Length == 0 && issue.Reason.Contains("not"));
        var request=Assert.Single(renderer.Requests);
        Assert.Contains("<d>[English] Do not cross the bridge.</d>",request.Prompt);
        Assert.Contains("verbatim and in order",request.SpeechInstruction);
        Assert.False(SceneVideoLoop.State(run).Clips[0].SpeechAccepted);
        // Explicit retry changes only this take, with the corrected words and a new durable key.
        await runs.ReplyAsync(run.Id,run.Revision,new(run.Question.Id,"english","The traveller says: \"Stay here.\""));
        run=await ActorTick(run.Id);
        production.Speech=new("detected","en",.99,"Stay here.","English speech");
        run=await ActorTick(run.Id);
        Assert.Equal(2,renderer.Requests.Count);
        Assert.Contains("<d>[English] Stay here.</d>",renderer.Requests[1].Prompt);
        Assert.DoesNotContain("<d>[English] Do not cross the bridge.</d>",renderer.Requests[1].Prompt);
        Assert.NotEqual(renderer.Keys[0],renderer.Keys[1]);
    }
    [Fact]
    public async Task Matching_non_English_speech_continues_without_forcing_English()
    {
        production.Speech = new("detected","fa",.97,"سلام دوست من","Persian speech");
        var run = await SpeechRun("fa");
        for (var i=0;i<30 && !run.IsTerminal && run.Status!="waiting";i++) run=await ActorTick(run.Id);
        Assert.Equal("completed",run.Status); Assert.Equal(2,renderer.Requests.Count);
        Assert.All(renderer.Requests,r=>Assert.Contains("Persian",r.SpeechInstruction));
    }
    [Fact]
    public async Task Speech_mismatch_waits_and_corrects_only_current_clip_with_a_new_key()
    {
        production.Speech = new("detected","fa",.98,"سلام دوست من","Persian speech");
        var run=await SpeechRun();
        for (var i=0;i<20 && run.Status!="waiting";i++) run=await ActorTick(run.Id);
        Assert.Equal("speech-review",run.Question!.Kind); Assert.Single(renderer.Requests);
        await runs.ReplyAsync(run.Id,run.Revision,new(run.Question.Id,null,"Please fix this"));
        run=await ActorTick(run.Id); Assert.Equal("waiting",run.Status); Assert.Single(renderer.Requests);
        await runs.ReplyAsync(run.Id,run.Revision,new(run.Question!.Id,"english","Say hello to my friend."));
        run=await ActorTick(run.Id);
        var state=SceneVideoLoop.State(run);
        Assert.Equal(0,state.Current); Assert.Equal(2,state.Clips[0].RenderAttempt);
        Assert.Equal($"{run.Id}-1-1",state.Clips[0].ReplacesJobKey);
        production.Speech=new("detected","en",.98,"Hello my friend","English speech");
        for (var i=0;i<30 && !run.IsTerminal && run.Status!="waiting";i++) run=await ActorTick(run.Id);
        Assert.Equal("completed",run.Status); Assert.Equal(3,renderer.Requests.Count);
        Assert.Contains("Say hello",renderer.Requests[1].SpeechInstruction);
        Assert.Equal($"{run.Id}-1-1",renderer.Requests[1].ReplacesJobKey);
        Assert.Equal($"{run.Id}-1-2",renderer.Requests[2].PreviousJobKey);
    }
    [Fact]
    public async Task Ununderstood_transcripts_require_a_human_even_with_matching_language()
    {
        audio.Understood=false;
        var run=await SpeechRun();
        for (var i=0;i<20 && run.Status!="waiting";i++) run=await ActorTick(run.Id);
        Assert.Equal("speech-review",run.Question!.Kind);
        Assert.Contains("could not confidently understand",run.Question.Prompt);
        Assert.Single(renderer.Requests);
        await runs.ReplyAsync(run.Id,run.Revision,new(run.Question.Id,"recheck",null));
        run=await ActorTick(run.Id); run=await ActorTick(run.Id);
        Assert.Equal("waiting",run.Status); Assert.Single(renderer.Requests);
    }
    [Fact]
    public async Task Ambiguous_story_language_asks_before_render_and_honors_human_language()
    {
        var run=await SpeechRun("auto");
        for(var i=0;i<5 && run.Status!="waiting";i++) run=await ActorTick(run.Id);
        Assert.Equal("speech-intent",run.Question!.Kind); Assert.Empty(renderer.Requests);
        audio.Language="es";
        await runs.ReplyAsync(run.Id,run.Revision,new(run.Question.Id,"correct","Spanish. Say Hola amigo."));
        run=await ActorTick(run.Id);
        Assert.Equal("es",SceneVideoLoop.State(run).ExpectedSpeechLanguage);
        Assert.Equal("plan",run.Phase); Assert.Empty(renderer.Requests);
        run=await ActorTick(run.Id);
        Assert.Contains("Spanish",planner.Clarifications);
    }
    [Fact]
    public async Task Speech_quality_corrections_stop_after_two_new_takes()
    {
        production.Speech=new("uncertain",null,.2,"","Speech is unclear");
        var run=await SpeechRun();
        for(var attempt=0;attempt<3;attempt++) {
            for(var i=0;i<25 && run.Status!="waiting";i++) run=await ActorTick(run.Id);
            Assert.Equal("speech-review",run.Question!.Kind);
            Assert.Equal(attempt+1,renderer.Requests.Count);
            if(attempt==2) {
                Assert.DoesNotContain(run.Question.Choices,c=>c.Id is "english" or "none" or "correct");
                Assert.Contains(run.Question.Choices,c=>c.Id=="accept");
            } else {
                await runs.ReplyAsync(run.Id,run.Revision,new(run.Question.Id,"english",null));
                run=await ActorTick(run.Id);
            }
        }
    }
    [Fact]
    public async Task Director_persists_and_uses_the_requested_project_across_actor_restarts()
    {
        var run=await ProductionRun(false);
        var lease=(await runs.ClaimAsync(TimeSpan.FromMinutes(1)))!;
        var state=SceneVideoLoop.State(lease.Run);
        var project=Guid.NewGuid().ToString("N");
        state.Input=state.Input with { ProjectId=project };
        lease.Run.State=JsonSerializer.SerializeToElement(state);
        await runs.SaveAsync(lease,"test","ready","Project captured");
        for(var i=0;i<25 && !run.IsTerminal;i++) run=await ActorTick(run.Id);
        Assert.Equal("completed",run.Status);
        Assert.Equal(project,production.RequestedProject);
        Assert.Equal(project,SceneVideoLoop.State(run).ProjectId);
        Assert.All(renderer.Requests,r=>Assert.Equal(project,r.ProjectId));
    }
    [Fact]
    public async Task Continue_replace_slot_forwards_final_mix_to_project_and_render()
    {
        var run = await ProductionRun(false);
        var lease = (await runs.ClaimAsync(TimeSpan.FromMinutes(1)))!;
        var state = SceneVideoLoop.State(lease.Run);
        state.StoryId = "story"; state.StoryName = "Replace slot";
        state.Input = state.Input with { StoryMode = "continue", ReplaceClipIndex = 2, ContinuityMedia = "final",
            ProjectId = Guid.NewGuid().ToString("N") };
        lease.Run.Phase = "project";
        lease.Run.State = JsonSerializer.SerializeToElement(state);
        await runs.SaveAsync(lease, "test", "ready", "Replace slot ready");
        for (var i = 0; i < 30 && !run.IsTerminal; i++) run = await ActorTick(run.Id);
        Assert.Equal("completed", run.Status);
        Assert.Equal(2, production.RequestedReplaceClipIndex);
        Assert.Equal("final", production.RequestedContinuityMedia);
        Assert.Null(production.RequestedKeepLaterClips);
        Assert.NotEmpty(renderer.Requests);
        Assert.All(renderer.Requests, r => {
            Assert.Equal("final", r.ContinuityMedia);
            Assert.Equal(2 + (r.ClipNumber - 1), r.ReplaceClipIndex);
            Assert.Null(r.KeepLaterClips);
        });
        Assert.Equal(2, renderer.Requests[0].ReplaceClipIndex);
    }
    [Fact]
    public async Task Continue_keep_later_replace_forwards_to_project_and_render()
    {
        var run = await ProductionRun(false);
        var lease = (await runs.ClaimAsync(TimeSpan.FromMinutes(1)))!;
        var state = SceneVideoLoop.State(lease.Run);
        state.StoryId = "story"; state.StoryName = "Keep later replace";
        state.Input = state.Input with { StoryMode = "continue", ReplaceClipIndex = 2, ContinuityMedia = "final",
            KeepLaterClips = true, ProjectId = Guid.NewGuid().ToString("N") };
        lease.Run.Phase = "project";
        lease.Run.State = JsonSerializer.SerializeToElement(state);
        await runs.SaveAsync(lease, "test", "ready", "Keep later ready");
        for (var i = 0; i < 30 && !run.IsTerminal; i++) run = await ActorTick(run.Id);
        Assert.Equal("completed", run.Status);
        Assert.Equal(2, production.RequestedReplaceClipIndex);
        Assert.Equal("final", production.RequestedContinuityMedia);
        Assert.True(production.RequestedKeepLaterClips);
        Assert.NotEmpty(renderer.Requests);
        Assert.All(renderer.Requests, r => {
            Assert.Equal("final", r.ContinuityMedia);
            Assert.Equal(2 + (r.ClipNumber - 1), r.ReplaceClipIndex);
            Assert.True(r.KeepLaterClips);
        });
    }
    [Fact]
    public async Task Unavailable_visual_review_does_not_block_speech_and_neither_approval_skips_the_other()
    {
        critic.Throw = true; production.Speech = new("uncertain",null,.1,"unclear","Speech is unclear");
        var run = await SpeechRun();
        for(var i=0;i<30 && run.Status!="waiting";i++) run=await ActorTick(run.Id);
        Assert.Equal("continuity",run.Question!.Kind);
        Assert.NotNull(SceneVideoLoop.State(run).Clips[0].Speech);
        Assert.Equal(1,production.SpeechCalls);
        Assert.Single(renderer.Requests);
        await runs.ReplyAsync(run.Id,run.Revision,new(run.Question.Id,"accept",null));
        run=await ActorTick(run.Id);
        Assert.Equal("speech-review",run.Question!.Kind);
        Assert.Equal("waiting",run.Status);
        Assert.Equal(0,SceneVideoLoop.State(run).Current);
        Assert.Equal(1,production.SpeechCalls); // Cached independently; no second inspection on approval.
        await runs.ReplyAsync(run.Id,run.Revision,new(run.Question.Id,"accept",null));
        run=await ActorTick(run.Id);
        Assert.Equal(1,SceneVideoLoop.State(run).Current);
        Assert.Single(renderer.Requests);
    }
    [Fact]
    public async Task Visual_acceptance_requires_separate_audio_choice_even_after_machine_pass_and_restart()
    {
        critic.Throw = true;
        production.Speech = new("detected","en",.99,"Hello my friend","English speech");
        var run = await SpeechRun();
        for(var i=0;i<30 && run.Status!="waiting";i++) run=await ActorTick(run.Id);
        Assert.Equal("continuity",run.Question!.Kind);
        Assert.True(SceneVideoLoop.State(run).Clips[0].SpeechVerified);
        await runs.ReplyAsync(run.Id,run.Revision,new(run.Question.Id,"accept",null));
        run=await ActorTick(run.Id); // ActorTick recreates runtime and store each time.
        Assert.Equal("speech-review",run.Question!.Kind);
        Assert.Equal(0,SceneVideoLoop.State(run).Current); Assert.Single(renderer.Requests);
        Assert.True(SceneVideoLoop.State(run).Clips[0].AudioReviewRequired);
        await runs.ReplyAsync(run.Id,run.Revision,new(run.Question.Id,null,"The speech still sounds wrong."));
        run=await ActorTick(run.Id);
        Assert.Equal("waiting",run.Status); Assert.Equal(0,SceneVideoLoop.State(run).Current);
        await runs.ReplyAsync(run.Id,run.Revision,new(run.Question!.Id,"recheck",null));
        run=await ActorTick(run.Id); run=await ActorTick(run.Id);
        Assert.Equal(2,production.SpeechCalls); Assert.Equal("speech-review",run.Question!.Kind);
        Assert.Equal(0,SceneVideoLoop.State(run).Current); Assert.Single(renderer.Requests);
        await runs.ReplyAsync(run.Id,run.Revision,new(run.Question.Id,"accept",null));
        run=await ActorTick(run.Id);
        Assert.Equal(1,SceneVideoLoop.State(run).Current);
        Assert.Equal("keep-original",SceneVideoLoop.State(run).Clips[0].AudioDecision);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Clip_audio_preview_waits_for_approval_survives_restart_and_retries_without_video(bool available)
    {
        critic.Throw=true; production.Speech=new("uncertain",null,.1,"gibberish","Unclear speech");
        var run=await SpeechRun();
        var lease=(await runs.ClaimAsync(TimeSpan.FromMinutes(1)))!;
        var state=SceneVideoLoop.State(lease.Run); state.AudioEnabled=true;
        state.Input=state.Input with {NarrationMode="dedicated"};
        lease.Run.State=JsonSerializer.SerializeToElement(state); await runs.SaveAsync(lease,"test","ready","Narration enabled");
        for(var i=0;i<30 && run.Status!="waiting";i++) run=await ActorTick(run.Id);
        await runs.ReplyAsync(run.Id,run.Revision,new(run.Question!.Id,"approve",null));
        run=await ActorTick(run.Id);
        for(var i=0;i<30 && run.Status!="waiting";i++) run=await ActorTick(run.Id);
        Assert.Equal("continuity",run.Question!.Kind);
        Assert.False(SceneVideoLoop.State(run).Clips[0].SpeechAccepted);
        production.PreviewAvailable=available;
        await runs.ReplyAsync(run.Id,run.Revision,new(run.Question.Id,"accept",null));
        run=await ActorTick(run.Id);
        Assert.Equal("clip-audio-plan",run.Phase);
        for(var i=0;i<10 && run.Status!="waiting";i++) run=await ActorTick(run.Id);
        Assert.Equal("clip-audio-review",run.Question!.Kind);
        Assert.Equal(available,run.Question.Choices.Any(c=>c.Id=="approve"));
        Assert.Equal(0,SceneVideoLoop.State(run).Current); Assert.Single(renderer.Requests);
        var question=run.Question.Id;
        await runs.ReplyAsync(run.Id,run.Revision,new(question,null,"Please make speech clear"));
        run=await ActorTick(run.Id);
        Assert.Equal("waiting",run.Status); Assert.Equal(1,production.PreviewCalls);
        Assert.Null(SceneVideoLoop.State(run).Clips[0].ApprovedAudioVersion);
        production.PreviewAvailable=true;
        await runs.ReplyAsync(run.Id,run.Revision,new(run.Question!.Id,"retry",null));
        run=await ActorTick(run.Id);
        for(var i=0;i<10 && run.Status!="waiting";i++) run=await ActorTick(run.Id);
        Assert.Equal(2,production.PreviewCalls); Assert.Single(renderer.Requests);
        await runs.ReplyAsync(run.Id,run.Revision,new(run.Question!.Id,"approve",null));
        run=await ActorTick(run.Id);
        Assert.Equal(1,SceneVideoLoop.State(run).Current);
        Assert.Equal("version-1",SceneVideoLoop.State(run).Clips[0].ApprovedAudioVersion);
        Assert.False(SceneVideoLoop.State(run).Clips[0].SpeechAccepted);
        Assert.Single(renderer.Requests); Assert.Empty(production.Deliveries);
    }
    private async Task<AgentRun> ReachFinalAudioReview(AgentRun run)
    {
        for(var i=0;i<80;i++) {
            run=await ActorTick(run.Id);
            if(run.Status!="waiting") continue;
            if(run.Question!.Kind!="clip-audio-review") return run;
            await runs.ReplyAsync(run.Id,run.Revision,new(run.Question.Id,"approve",null));
        }
        throw new InvalidOperationException("Audio review did not finish");
    }
    [Fact]
    public async Task Dedicated_narration_is_approved_before_render_and_does_not_rerender_bad_native_speech()
    {
        var run=await SpeechRun();
        var lease=(await runs.ClaimAsync(TimeSpan.FromMinutes(1)))!;
        var state=SceneVideoLoop.State(lease.Run); state.AudioEnabled=true;
        state.Input=state.Input with { NarrationMode="dedicated" };
        lease.Run.State=JsonSerializer.SerializeToElement(state);
        await runs.SaveAsync(lease,"test","ready","Narration enabled");
        production.Speech=new("uncertain",null,.1,"","Bad native speech");
        for(var i=0;i<30 && run.Status!="waiting";i++) run=await ActorTick(run.Id);
        Assert.Equal("review",run.Question!.Kind); Assert.Empty(renderer.Requests);
        Assert.All(SceneVideoLoop.State(run).Clips,c=>Assert.Equal("A journey begins.",c.NarrationText));
        await runs.ReplyAsync(run.Id,run.Revision,new(run.Question.Id,"approve",null));
        run=await ActorTick(run.Id);
        run=await ReachFinalAudioReview(run);
        Assert.Equal("audio-review",run.Question!.Kind);
        Assert.Equal(2,renderer.Requests.Count);
        Assert.All(renderer.Requests,r=>Assert.Contains("added separately",r.SpeechInstruction));
        Assert.Equal(new[]{"A journey begins.","A journey begins."},production.AudioPlan!.Narration!.Scripts);
        Assert.Empty(production.Deliveries);
        Assert.Equal(new[]{"version-1","version-2"}, production.AudioPlan!.Approved!.Select(x=>x.Version));
        Assert.All(SceneVideoLoop.State(run).Clips,c=>Assert.NotNull(c.ApprovedAudioVersion));
        await runs.ReplyAsync(run.Id,run.Revision,new(run.Question.Id,"improved",null));
        run=await ActorTick(run.Id); run=await ActorTick(run.Id);
        Assert.Equal("completed",run.Status); Assert.Equal(2,renderer.Requests.Count);
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Existing_clip_can_switch_to_narration_without_resubmitting_video(bool hasOriginalScript, bool cloud)
    {
        const string original = "Every evening, she climbs these stairs. Beyond the shore, someone is still finding their way home.";
        if (hasOriginalScript) planner.Spoken = "EXT. LIGHTHOUSE. (NARRATION: " + original + ")";
        production.CloudNarration = cloud;
        var run=await SpeechRun();
        var lease=(await runs.ClaimAsync(TimeSpan.FromMinutes(1)))!;
        var state=SceneVideoLoop.State(lease.Run); state.AudioEnabled=true;
        lease.Run.State=JsonSerializer.SerializeToElement(state); await runs.SaveAsync(lease,"test","ready","Audio enabled");
        production.Speech=new("uncertain",null,.1,"","Unclear speech");
        for(var i=0;i<30 && run.Status!="waiting";i++) run=await ActorTick(run.Id);
        Assert.Equal("speech-review",run.Question!.Kind);
        await runs.ReplyAsync(run.Id,run.Revision,new(run.Question.Id,"narration",null));
        run=await ActorTick(run.Id); run=await ActorTick(run.Id);
        Assert.Equal("narration-review",run.Question!.Kind); Assert.Single(renderer.Requests);
        Assert.Equal(hasOriginalScript ? original : "A journey begins.", SceneVideoLoop.State(run).Clips[0].NarrationText);
        if (hasOriginalScript) Assert.Equal("", SceneVideoLoop.State(run).Clips[1].NarrationText);
        Assert.Equal(cloud, run.Question.Choices.Any(c => c.Id == "approve-cloud"));
        await runs.ReplyAsync(run.Id,run.Revision,new(run.Question.Id,cloud ? "approve-cloud" : "approve",null));
        run=await ActorTick(run.Id);
        Assert.Equal("narration-background",run.Question!.Kind);
        Assert.Equal(cloud,run.Question.Choices.Any(c=>c.Id=="matching" && c.Disabled != true));
        if (!cloud) await Assert.ThrowsAsync<ArgumentException>(()=>runs.ReplyAsync(run.Id,run.Revision,new(run.Question.Id,"matching",null)));
        await runs.ReplyAsync(run.Id,run.Revision,new(run.Question.Id,cloud ? "matching" : "preserve",null));
        run=await ActorTick(run.Id);
        Assert.Equal(0,SceneVideoLoop.State(run).Current); Assert.Single(renderer.Requests);
        Assert.Equal("clip-audio-plan",run.Phase);
        for(var i=0;i<10 && run.Status!="waiting";i++) run=await ActorTick(run.Id);
        Assert.Equal("clip-audio-review",run.Question!.Kind);
        Assert.Equal(cloud ? "eleven_multilingual_v2" : "kokoro", production.AudioPlan!.Narration!.Model);
        Assert.Equal(cloud ? "configured" : "af_heart", production.AudioPlan.Narration.Voice);
        Assert.Equal(0.2, production.AudioPlan.Narration.Windows![0].StartSeconds);
        Assert.Null(production.AudioPlan.Narration.Windows[0].TargetSeconds);
        Assert.Null(production.AudioPlan.Narration.Windows[0].PaceOverride);
        Assert.Equal(cloud ? "matching" : null, production.AudioPlan.BackgroundMusic);
        Assert.Equal(1,SceneVideoLoop.State(run).Clips[0].RenderAttempt);
        Assert.Contains(run.Question.Choices,c=>c.Id=="balance");
        await runs.ReplyAsync(run.Id,run.Revision,new(run.Question.Id,"balance","{\"speechDb\":-3,\"musicDb\":-6,\"continuousMusic\":true}"));
        run=await ActorTick(run.Id);
        for(var i=0;i<10 && run.Status!="waiting";i++) run=await ActorTick(run.Id);
        Assert.Equal("clip-audio-review",run.Question!.Kind);
        Assert.Equal(new SceneAudioMix(-3,-6),production.AudioPlan!.Mix);
        Assert.True(production.AudioPlan.ContinuousMusic);
        Assert.Single(renderer.Requests);
        Assert.Equal(hasOriginalScript ? original : "A journey begins.", production.AudioPlan.Narration!.Scripts[0]);
        if (cloud) {
            Assert.Contains(run.Question.Choices,c=>c.Id=="music");
            await runs.ReplyAsync(run.Id,run.Revision,new(run.Question.Id,"music",null));
            run=await ActorTick(run.Id);
            Assert.Equal("narration-background",run.Question!.Kind);
            await runs.ReplyAsync(run.Id,run.Revision,new(run.Question.Id,"matching-clean",null));
            run=await ActorTick(run.Id);
            for(var i=0;i<10 && run.Status!="waiting";i++) run=await ActorTick(run.Id);
            Assert.Equal("clean",production.AudioPlan!.MusicProfile);
            Assert.Single(renderer.Requests);
            await runs.ReplyAsync(run.Id,run.Revision,new(run.Question!.Id,"music",null));
            run=await ActorTick(run.Id);
            await runs.ReplyAsync(run.Id,run.Revision,new(run.Question!.Id,"preserve",null));
            run=await ActorTick(run.Id);
            for(var i=0;i<10 && run.Status!="waiting";i++) run=await ActorTick(run.Id);
            Assert.Equal("clip-audio-review",run.Question!.Kind);
            Assert.Null(production.AudioPlan!.BackgroundMusic);
            Assert.Equal(original,SceneVideoLoop.State(run).Clips[0].NarrationText);
            Assert.Single(renderer.Requests);
        }

    }
    [Fact]
    public async Task Dedicated_narration_does_not_silently_translate_another_requested_language()
    {
        var run=await SpeechRun("fa");
        var lease=(await runs.ClaimAsync(TimeSpan.FromMinutes(1)))!;
        var state=SceneVideoLoop.State(lease.Run); state.AudioEnabled=true; state.Input=state.Input with { NarrationMode="dedicated" };
        lease.Run.State=JsonSerializer.SerializeToElement(state); await runs.SaveAsync(lease,"test","ready","Audio enabled");
        for(var i=0;i<20 && run.Status!="waiting";i++) run=await ActorTick(run.Id);
        Assert.Equal("narration-language",run.Question!.Kind); Assert.Empty(renderer.Requests);
        Assert.Equal("fa",SceneVideoLoop.State(run).ExpectedSpeechLanguage);
    }
    [Fact]
    public async Task Legacy_visual_wait_gets_speech_observation_without_consuming_human_approval()
    {
        critic.Status="uncertain";
        var existing=await SpeechRun();
        for(var i=0;i<30 && existing.Status!="waiting";i++) existing=await ActorTick(existing.Id);
        var state=SceneVideoLoop.State(existing); state.Clips[0].Speech=null; state.Clips[0].ChecksVersion=0;
        await runs.CancelAsync(existing.Id); // Free the workspace before seeding a legacy checkpoint.
        var old=await runs.CreateAsync(new() { RequestId=Guid.NewGuid().ToString(),RequestHash="legacy",WorkspaceId=existing.WorkspaceId,
            Workflow="scene-video",ScenarioId=existing.ScenarioId,Status="waiting",Phase="critic",Question=existing.Question,State=JsonSerializer.SerializeToElement(state) });
        var calls=production.SpeechCalls;
        old=await ActorTick(old.Id);
        Assert.Equal("waiting",old.Status); Assert.Equal("review-wait",old.Phase);
        Assert.Equal(existing.Question!.Id,old.Question!.Id);
        Assert.Equal(calls+1,production.SpeechCalls);
        Assert.False(SceneVideoLoop.State(old).Clips[0].VisualAccepted);
        Assert.NotNull(SceneVideoLoop.State(old).Clips[0].Speech);
        Assert.Single(renderer.Requests);
        Assert.Null(await runs.ClaimAsync(TimeSpan.FromMinutes(1),monitors:SceneVideoLoop.WaitMonitors));
    }
    [Fact]
    public async Task Structured_routes_survive_restart_and_are_scoped_to_the_run()
    {
        var run = await ProductionRun();
        for(var i=0;i<15 && run.Status!="waiting";i++) run=await ActorTick(run.Id);
        var reopened = new SqliteAgentRunStore(Path.Combine(dir,"runs.db"));
        var events = await reopened.EventsAsync(run.Id);
        var routed=events.Where(e=>e.Route!=null).ToArray();
        Assert.Contains(routed,e=>e.Route!.From=="Director" && e.Route.To=="Planner");
        Assert.Contains(routed,e=>e.Route!.From=="Planner" && e.Route.To=="Director");
        Assert.Contains(routed,e=>e.Route!.To=="Human");
        Assert.All(routed,e=>Assert.Equal(run.Id,e.RunId));
        Assert.Equal(routed.Select(e=>e.Sequence).Distinct().Count(),routed.Length);
        Assert.Empty(await reopened.EventsAsync("another-run"));
        Assert.Empty(await reopened.EventsAsync(run.Id,events.Last().Sequence));
        await runs.ReplyAsync(run.Id,run.Revision,new(run.Question!.Id,"approve",null));
        var answer=Assert.Single(await reopened.EventsAsync(run.Id,events.Last().Sequence));
        Assert.Equal("Human",answer.Route!.From); Assert.Equal("Director",answer.Route.To);
    }
    private sealed class Audio : ISceneAudioEngineer {
        public Task<IReadOnlyList<string>> NarrationAsync(string prompt,string model,IReadOnlyList<SceneVideoClip> clips,CancellationToken ct)=>Task.FromResult<IReadOnlyList<string>>(clips.Select(_=>"A journey begins.").ToArray());
        public string Language="unknown";
        public Task<string> SpeechLanguageAsync(string prompt,string model,CancellationToken ct)=>Task.FromResult(Language);
        public bool Understood=true;
        public Task<bool> SpeechUnderstoodAsync(string prompt,string model,SceneSpeechReport speech,CancellationToken ct)=>Task.FromResult(Understood);
        public Task<SceneAudioPlan> PlanAsync(string prompt, string model, CancellationToken ct) => Task.FromResult(new SceneAudioPlan(true, "Continuous music"));
        public Task<SceneContinuityReport> ReviewAsync(string prompt, string model, SceneAudioDraft draft, CancellationToken ct) => Task.FromResult(new SceneContinuityReport("uncertain", "Compare the versions", []));
    }
    private sealed class Production : ISceneVideoProduction
    {
        public bool CloudNarration;
        public Task<bool> MatchingMusicAvailableAsync(CancellationToken ct) => Task.FromResult(CloudNarration);
        public Task<IReadOnlyList<string>> NarrationModelsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(CloudNarration ? ["kokoro", "eleven_multilingual_v2"] : ["kokoro"]);
        public string? RequestedProject; public int? RequestedReplaceClipIndex; public string? RequestedContinuityMedia; public bool? RequestedKeepLaterClips;
        public int SpeechCalls; public SceneAudioPlan? AudioPlan;
        public SceneSpeechReport Speech = new("detected","en",.98,"Hello my friend","English speech");
        public SceneMotionSummary? EndingMotion;
        public bool Shifted;
        public Task<SceneVideoEvidence> EvidenceAsync(string key, bool recheck, CancellationToken ct) { Shifted = recheck; return EvidenceAsync(key, ct); }
        public Task<SceneSpeechReport> InspectSpeechAsync(string key,CancellationToken ct) { SpeechCalls++; return Task.FromResult(Speech); }
        public int PreviewCalls; public bool PreviewAvailable = true;
        public Task<SceneAudioDraft> PrepareClipAudioAsync(string run, string project, IReadOnlyList<string> keys, SceneAudioPlan plan, CancellationToken ct) {
            PreviewCalls++; AudioPlan = plan;
            return Task.FromResult(new SceneAudioDraft(PreviewAvailable, "Clip audio prepared", JsonSerializer.SerializeToElement(new { }), PreviewAvailable ? "version-" + keys.Count : null));
        }
        public bool AudioAvailable = true; public int AudioCalls; public string? AudioChoice;
        public Task<SceneAudioDraft> PrepareAudioAsync(string run, string project, IReadOnlyList<string> keys, SceneAudioPlan plan, CancellationToken ct) { AudioCalls++; AudioPlan=plan; return Task.FromResult(new SceneAudioDraft(AudioAvailable, "Compared audio", JsonSerializer.SerializeToElement(new { }))); }
        public Task<SceneVideoDelivery> FinishAudioAsync(string run, string project, IReadOnlyList<string> keys, string choice, CancellationToken ct) { AudioChoice=choice; return FinishAsync(run, project, keys, ct); }
        public List<IReadOnlyList<string>> Deliveries = [];
        public Task<SceneVideoProject> EnsureProjectAsync(string run, string story, string mode, string name, CancellationToken ct,
            string? projectId = null, int? replaceClipIndex = null, string? continuityMedia = null, bool? keepLaterClips = null) {
            RequestedProject = projectId; RequestedReplaceClipIndex = replaceClipIndex; RequestedContinuityMedia = continuityMedia;
            RequestedKeepLaterClips = keepLaterClips;
            return Task.FromResult(new SceneVideoProject(projectId ?? "project"));
        }
        public Task<SceneVideoEvidence> EvidenceAsync(string key, CancellationToken ct) => Task.FromResult(new SceneVideoEvidence(["start", "middle", "end"], Opening: new(["o1", "o2", "o3"], [0, .5, 1]), Ending: new(["e1", "e2", "e3"], [13.5, 14.25, 14.95]), EndingMotion: EndingMotion));
        public Task<SceneVideoDelivery> FinishAsync(string run, string project, IReadOnlyList<string> keys, CancellationToken ct) {
            Deliveries.Add(keys); return Task.FromResult(new SceneVideoDelivery("https://cynelo.test/finished.mp4"));
        }
    }
    private sealed class Critic : ISceneVideoCritic
    {
        public string Status = "passed"; public string EndingStatus = "observed"; public int Calls; public bool Throw; public string? LastPrompt;
        public Task<SceneContinuityReport> CheckAsync(string prompt, SceneVideoEvidence current, SceneVideoEvidence? previous, CancellationToken ct) {
            Calls++; LastPrompt = prompt; if(Throw) throw new HttpRequestException("Offline"); return Task.FromResult(new SceneContinuityReport(Status, "Sampled-frame review", Status == "passed" ? [] : ["Check the outfit"], new(EndingStatus, "Inside greenhouse", "Moving toward bench", "Pot in both arms", "Entered greenhouse", "Camera follows from behind"), previous == null ? "not_applicable" : Status, "fake-vision"));
        }
    }
    private sealed class Planner : ISceneVideoPlanner
    {
        public string? Spoken;
        public string? Clarifications;
        public string[] ContextNames = [];
        public Task<SceneVideoPlan> PlanAsync(SceneWorkspace workspace, SceneVideoState state, IReadOnlyList<int> durations, CancellationToken ct)
        { ContextNames = workspace.Entities.Select(e => e.Name).ToArray(); Clarifications = state.Clarifications; return Task.FromResult(new SceneVideoPlan(durations.Select((_, i) => i == 0 && Spoken != null ? Spoken : $"The traveller advances to stage {i + 1}.").ToArray())); }
    }
    private sealed class Interpreter : IScenePromptInterpreter
    {
        public Exception? NextError;
        public int Resets;
        public bool WaitForCancellation;
        public Task<ScenePromptExtraction> InterpretAsync(SceneWorkspace w, string prompt, string model,
            IProgress<ScenePromptProgress>? progress, CancellationToken ct)
        {
            progress?.Report(new("section_complete", 1, 2, 1, "private model text"));
            progress?.Report(new("continuity", 0, 0, 2, "private model text"));
            progress?.Report(new("continuity", 0, 0, 2, "private model text"));
            return InterpretAsync(w, prompt, model, ct);
        }
        public Task ResetAsync(SceneWorkspace w, string p, string m, CancellationToken ct) { Resets++; return Task.CompletedTask; }
        private static async Task<ScenePromptExtraction> WaitAsync(CancellationToken ct) { await Task.Delay(Timeout.Infinite, ct); return new([], [], []); }
        public bool RelationshipInValue;
        public List<int> HistorySizes = [];
        public List<string[]> ContextNames = [];
        public Task<ScenePromptExtraction> InterpretAsync(SceneWorkspace w, string prompt, string model, CancellationToken ct)
        {
            if (WaitForCancellation) return WaitAsync(ct);
            if (NextError is { } error) { NextError = null; throw error; }
            ContextNames.Add(w.Entities.Select(e => e.Name).ToArray());
            HistorySizes.Add(w.Prompts?.Count ?? 0);
            var entity = w.Entities.FirstOrDefault();
            if (RelationshipInValue && entity != null)
                return Task.FromResult(new ScenePromptExtraction([new("can", "Watering can", "Object")],
                    [new("can", "held_by", entity.Id, "", false, prompt),
                     new("can", "note", entity.Id, "", false, prompt)], []));
            return Task.FromResult(new ScenePromptExtraction(entity == null ? [new("traveller", "Traveller", "Character")] : [],
                [new(entity?.Id ?? "traveller", "status", "stage " + HistorySizes.Count, "", false, prompt)], []));
        }
    }
    [Theory]
    [InlineData("continue")]
    [InlineData("new")]
    public async Task Story_mode_reaches_renderer_on_first_clip_and_after_restart(string mode)
    {
        var run = await Create(seconds: 30);
        var state = SceneVideoLoop.State(run); state.Input = state.Input with { StoryMode = mode };
        var lease = (await runs.ClaimAsync(TimeSpan.FromMinutes(1)))!;
        lease.Run.State = JsonSerializer.SerializeToElement(state); await runs.SaveAsync(lease, "test", "setup", "Ready");
        for (var step = 0; step < 20 && !run.IsTerminal; step++) run = await Tick(run.Id);
        Assert.Equal("completed", run.Status);
        Assert.Equal(2, renderer.Requests.Count);
        Assert.All(renderer.Requests, request => Assert.Equal(mode, request.StoryMode));
    }
    [Fact]
    public async Task Offline_monitor_replaces_unclear_question_then_recovers_same_job_without_submission()
    {
        var run=await WaitingRun(kind:"retry");
        renderer.Intervention=new("ComfyUI is not responding. Start ComfyUI, then check again.","comfyui_unavailable");
        run=await Tick(run.Id);
        Assert.Equal(renderer.Intervention.Message,run.Question!.Prompt);
        Assert.Equal(new[] { "retry", "restart" }, run.Question.Choices.Select(c => c.Id));
        var questionId=run.Question.Id;
        MakeDue(run.Id); run=await Tick(run.Id);
        Assert.Equal(questionId,run.Question!.Id); Assert.Empty(renderer.Requests);
        renderer.Intervention=null;
        MakeDue(run.Id); run=await Tick(run.Id);
        Assert.Equal("running",run.Status); Assert.Null(run.Question);
        Assert.Equal("existing-job",SceneVideoLoop.State(run).Clips[0].ProviderJobId);
        Assert.Empty(renderer.Requests);
    }
    [Fact]
    public async Task Reset_current_clip_preserves_completed_clip_and_retries_one_durable_reset_after_lost_response()
    {
        var run = await WaitingRun(kind: "retry");
        var lease = (await runs.ClaimAsync(TimeSpan.FromMinutes(1), monitors: SceneVideoLoop.WaitMonitors))!;
        var state = SceneVideoLoop.State(lease.Run); state.Current = 1;
        state.Clips[0].Status = "completed"; state.Clips[0].VideoUrl = "https://cynelo.test/keep.mp4";
        state.Clips[0].LastFrameUrl = "https://cynelo.test/keep.jpg";
        var completed = JsonSerializer.Serialize(state.Clips[0]);
        state.Clips[1].ProviderJobId = "existing-job"; state.Clips[1].Status = "rendering";
        lease.Run.State = JsonSerializer.SerializeToElement(state);
        await runs.SaveAsync(lease, "test", "setup", "Current clip interrupted");
        renderer.Intervention = new("Cannot match saved provider job", "recovery_uncertain");
        MakeDue(run.Id); run = await Tick(run.Id);
        Assert.Contains(run.Question!.Choices, c => c.Id == "restart");
        run = await runs.ReplyAsync(run.Id, run.Revision, new(run.Question.Id, "restart", null));
        run = await Tick(run.Id); Assert.Equal("reset-render", run.Phase); Assert.Empty(renderer.ResetKeys);
        renderer.LoseResetResponse = true;
        run = await Tick(run.Id); Assert.Equal("reset-render", run.Phase);
        Assert.Equal(1, SceneVideoLoop.State(run).Clips[1].RenderAttempt);
        renderer.LoseResetResponse = false; MakeDue(run.Id); run = await Tick(run.Id);
        state = SceneVideoLoop.State(run);
        Assert.Equal("submit", run.Phase); Assert.Equal(2, state.Clips[1].RenderAttempt);
        Assert.Equal(completed, JsonSerializer.Serialize(state.Clips[0]));
        Assert.Equal(2, renderer.ResetKeys.Count); Assert.Single(renderer.ResetKeys.Distinct());
        Assert.Empty(renderer.Requests);
        Assert.Equal("ready", state.Clips[1].Status);
        Assert.Null(state.Clips[1].ProviderJobId);
    }
    [Fact]
    public async Task Missing_provider_id_reattaches_via_stable_request_key_without_new_render()
    {
        var run = await WaitingRun(kind: "retry");
        var lease = (await runs.ClaimAsync(TimeSpan.FromMinutes(1), monitors: SceneVideoLoop.WaitMonitors))!;
        var state = SceneVideoLoop.State(lease.Run); state.Clips[0].ProviderJobId = null;
        lease.Run.State = JsonSerializer.SerializeToElement(state); lease.Run.Phase = "submit";
        await runs.SaveAsync(lease, "test", "setup", "Lost submit response");
        MakeDue(run.Id); run = await Tick(run.Id);
        Assert.Equal("running", run.Status); Assert.Null(run.Question);
        Assert.Equal("existing-job", SceneVideoLoop.State(run).Clips[0].ProviderJobId);
        Assert.Empty(renderer.Requests);
    }
    private sealed class Renderer : ISceneVideoRenderer
    {
        public List<string> ResetKeys = [];
        public bool LoseResetResponse;
        public Task<SceneRenderJob> ResetAsync(string key, CancellationToken ct) {
            ResetKeys.Add(key);
            if (LoseResetResponse) throw new HttpRequestException("Reset accepted but response lost");
            return Task.FromResult(new SceneRenderJob("existing-job", "reset"));
        }

        public bool IsConfigured { get; set; } = true;
        public bool LoseResponse;
        public bool ReportQueued;
        public string PollStatus = "running";
        public bool PollFailure;
        public SceneRenderInterventionException? Intervention;
        public SceneRenderProgress? PollProgress;
        public int CheckCount;
        public List<string> Keys = []; public List<SceneRenderRequest> Requests = [];
        public Task<SceneRenderJob> EnsureAsync(string key, SceneRenderRequest request, CancellationToken ct)
        {
            Keys.Add(key); Requests.Add(request);
            if (LoseResponse) throw new HttpRequestException("Lost response after submission");
            if (ReportQueued) return Task.FromResult(new SceneRenderJob(key, "queued", References: new(8, 10, 5)));
            return Task.FromResult(new SceneRenderJob(key, "completed", $"https://cynelo.test/video/{request.ClipNumber}.mp4", $"https://cynelo.test/frame/{request.ClipNumber}.jpg"));
        }
        public Task<SceneRenderJob> GetAsync(string key, CancellationToken ct)
        {
            CheckCount++;
            if (Intervention != null) throw Intervention;
            if (PollFailure) throw new HttpRequestException("private upstream detail");
            return Task.FromResult(new SceneRenderJob("existing-job", PollStatus, "https://cynelo.test/completed.mp4", "https://cynelo.test/last.jpg", Progress: PollProgress));
        }
    }
    private sealed class Monitor : IOptionsMonitor<SceneVideoOptions>
    {
        public SceneVideoOptions CurrentValue { get; } = new();
        public SceneVideoOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<SceneVideoOptions, string?> listener) => null;
    }
}
