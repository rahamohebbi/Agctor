using System.Text.Json;
using AgctorSDK.Core.SceneMemory;
using AgctorSDK.Core.Workflows;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Host.Services.VideoWorkflow;

/// <summary>One bounded, checkpointed transition. External writes use stable identifiers across retries.</summary>
public sealed class SceneVideoLoop(IAgentRunStore runs, ISceneMemoryStore memory, ISceneVideoPlanner planner,
    IScenePromptInterpreter interpreter, ISceneVideoRenderer renderer, IOptionsMonitor<SceneVideoOptions> options,
    ILogger<SceneVideoLoop> log, ISceneVideoStoryGateway? stories = null, ISceneVideoProduction? production = null, ISceneVideoCritic? critic = null, ISceneAudioEngineer? audio = null)
{
    public static IReadOnlyList<AgentWaitMonitor> WaitMonitors { get; } =
        [new("scene-video", "poll", "timeout"), new("scene-video", "poll", "retry"), new("scene-video", "critic", "continuity"), new("scene-video", "speech-check", "continuity")];
    public static string Role(AgentRun run) => run.Reply != null ? "Director" : run.Phase switch
    { "narration-plan" or "speech-intent" or "speech-check" or "audio-plan" or "audio-process" or "audio-review" => "Audio Engineer", "audio-decision" => "Director", "plan" or "plan-review" => "Planner", "project" => "Project agent", "critic" => "Continuity critic", "finish" => "Editor agent", "prepare" or "commit" => "Scene Memory agent", "submit" or "poll" => "Render agent", _ => "Director" };
    private static string DescribeStep(string phase) => phase switch {
        "plan" => "Plan the sequence.", "plan-review" => "Check the shot plan.",
        "project" => "Prepare the story’s project.", "initialize" => "Create the story’s memory.",
        "prepare" => "Check the next shot against Scene Memory.", "commit" => "Save the approved shot.",
        "submit" => "Start the approved clip render.", "poll" => "Check the existing render’s progress.",
        "critic" => "Review visual continuity.", "speech-intent" => "Identify the requested spoken language.",
        "speech-check" => "Inspect the speech and language.", "narration-plan" => "Prepare narration for approval.",
        "audio-plan" => "Plan the story soundtrack.", "audio-process" => "Prepare the audio tracks.",
        "audio-review" => "Review the sound and speech evidence.", "audio-decision" => "Request an audio choice.",
        "finish" => "Assemble the approved film.", _ => "Continue the current step."
    };
    public static SceneVideoState State(AgentRun run) => run.State.Deserialize<SceneVideoState>()!;
    public static string RenderKey(AgentRun run, SceneVideoState state) => $"{run.Id}-{state.Current + 1}-{state.Clips[state.Current].RenderAttempt}";
    public async Task StepAsync(AgentRunLease lease, CancellationToken ct)
    {
        var run = lease.Run; var state = State(run); var role = Role(run);
        using var trace = SceneVideoTrace.Begin(runs, lease, run.Status == "waiting" && run.Reply == null ? run.Question?.Kind == "continuity" ? "Audio Engineer" : "Render agent" : role, log);
        if (state.Current < state.Clips.Count && state.Clips[state.Current] is { ChecksVersion: 0 } legacy &&
            (run.Phase == "speech-check" || run.Question?.Kind == "speech-review")) {
            // Before independent checks, reaching speech review proved visual approval.
            legacy.VisualAccepted = true; legacy.ChecksVersion = 1;
        }
        if (run.Status == "waiting" && run.Reply == null)
        {
            await ObserveWaitingAsync(lease, state, ct);
            return;
        }
        run.Status = "running"; run.DueAt = DateTimeOffset.UtcNow;
        var polling = run.Reply == null && run.Phase == "poll";
        var dispatchedPhase = run.Phase; var exchangeId = Guid.NewGuid().ToString("N");
        await runs.ActivityAsync(lease, "Director", $"Routing to {role}: {DescribeStep(run.Phase)}", ct,
            role == "Director" ? null : new("Director", role, dispatchedPhase, exchangeId));
        string summary;
        try
        {
            if (run.Reply != null) summary = await ResumeAsync(run, state, ct);
            else summary = await AdvanceAsync(run, state, ct);
            run.Failures = 0;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (SceneRenderInterventionException ex)
        {
            summary = ex.Message;
            Ask(run, "retry", summary, [new("retry", "Ready — retry this step")]);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Scene video run {RunId} failed during {Phase}", run.Id, run.Phase);
            run.Failures++;
            // Never expose upstream response bodies, URLs or credentials in the public activity feed.
            var reason = FailureReason(ex);
            summary = $"{reason} (Attempt {run.Failures}/{state.MaxAttempts}.)";
            if (run.Failures >= state.MaxAttempts)
                Ask(run, "retry", summary + " Your saved work is preserved. Retry this step when ready.", [new("retry", "Retry this step")]);
            else run.DueAt = DateTimeOffset.UtcNow.AddSeconds(Math.Min(30, Math.Pow(2, run.Failures)));
        }
        run.State = JsonSerializer.SerializeToElement(state);
        await runs.SaveAsync(lease, role, polling && run.Status == "running" ? "observation" : run.Status, run.Question?.Prompt ?? summary, ct, role == "Director" ? null : new(role, "Director", dispatchedPhase, exchangeId));
    }

    // Map validated categories to public copy. Model output and transport exception text are untrusted.
    private static string FailureReason(Exception error) => error switch
    {
        ScenePromptInterpretationException interpretation when interpretation.Attempts.SelectMany(a => a.Issues)
            .Any(i => i.Code.EndsWith("timeout", StringComparison.Ordinal)) =>
            "The scene model took too long to check the plan",
        ScenePromptInterpretationException interpretation when interpretation.Attempts.LastOrDefault()?.Issues
            .Any(i => i.Code == "invalid_participants") == true =>
            "The scene model could not link an action or sound to a valid participant after correction attempts",
        ScenePromptInterpretationException => "The scene model returned an inconsistent plan after correction attempts",
        HttpRequestException => "The required service could not be reached or returned an error",
        TimeoutException or OperationCanceledException => "The current step took too long to respond",
        _ => "The current step could not be completed safely"
    };

    private async Task<string> AdvanceAsync(AgentRun run, SceneVideoState state, CancellationToken ct)
    {
        if (run.Phase != "poll" && ++run.Steps > 512)
        { run.Status = "failed"; return "The workflow reached its step limit. Completed clips remain available."; }
        var audioKeys = state.Clips.Select((c, i) => $"{run.Id}-{i + 1}-{c.RenderAttempt}").ToArray();
        if (run.Phase == "audio-plan")
        {
            state.AudioPlan = await audio!.PlanAsync(System.Text.Json.JsonSerializer.Serialize(new { story = state.Input.Prompt, clarifications = state.Clarifications, previousContinuousMusic = state.PreviousContinuousMusic }), state.Model, ct);
            state.AudioPlan = state.AudioPlan with { Separator = state.Input.AudioModel ?? "roformer",
                Narration = Dedicated(state) ? new("kokoro", "af_heart", "en", state.Clips.Select(c => c.NarrationText ?? "").ToArray()) : null };
            run.Phase = "audio-process";
            return "The audio plan is ready. Preparing a shared soundtrack; original clips are preserved.";
        }
        if (run.Phase == "audio-process")
        {
            state.AudioDraft = await production!.PrepareAudioAsync(run.Id, state.ProjectId!, audioKeys, state.AudioPlan!, ct);
            run.Phase = "audio-review";
            return state.AudioDraft.Summary;
        }
        if (run.Phase == "audio-review")
        {
            state.AudioReview = await audio!.ReviewAsync(state.Input.Prompt, state.Model, state.AudioDraft!, ct);
            run.Phase = "audio-decision";
            return "Audio Engineer returned its inspection to the Director. " + state.AudioReview.Summary;
        }
        if (run.Phase == "audio-decision")
        {
            var choices = new List<AgentChoice>();
            if (state.AudioDraft!.ImprovedAvailable) choices.Add(new("improved", "Use improved audio"));
            choices.Add(new("original", "Keep original audio"));
            choices.Add(new("retry", "Check audio again"));
            if (Dedicated(state)) choices.Add(new("narration", "Revise narration · keep video"));
            Ask(run, "audio-review", state.AudioDraft.ImprovedAvailable
                ? "Compare the original and improved audio, then choose what sounds best. Music separation can affect background sounds."
                : "Audio improvement was unavailable. Your original video is safe. Retry the audio step or keep the original.", choices);
            return "The Director is waiting for your audio choice.";
        }
        if (run.Phase == "narration-plan")
        {
            if (state.ExpectedSpeechLanguage != "en")
            {
                Ask(run, "narration-language", "Dedicated narration currently supports English. Choose English narration, or keep the video's own speech for another language.",
                    [new("english", "Use English narration"), new("native", "Keep video-generated speech"), new("cancel", "Stop here")]);
                return "The narration language needs your choice.";
            }
            var scripts = await audio!.NarrationAsync(state.Input.Prompt + "\n" + state.Clarifications, state.Model, state.Clips, ct);
            if (scripts.Count != state.Clips.Count || scripts.All(string.IsNullOrWhiteSpace) || scripts.Any(s => s == null || s.Length > 1500)) throw new InvalidOperationException("Invalid narration script.");
            for (var i = 0; i < scripts.Count; i++) state.Clips[i].NarrationText = scripts[i];
            if (state.NarrationReturnPhase != null) {
                run.Phase = state.NarrationReturnPhase;
                Ask(run, "narration-review", "Review the exact narration for each clip. Approving replaces the video’s speech with one English voice; your video frames are kept.",
                    [new("approve", "Approve narration"), new("revise", "Revise narration"), new("cancel", "Stop here")]);
            } else run.Phase = "plan-review";
            return "The narration script is ready. You will approve the exact words before rendering.";
        }
        if (run.Phase == "speech-intent")
        {
            if (state.ExpectedSpeechLanguage == "auto")
                state.ExpectedSpeechLanguage = await IdentifySpeechLanguage(state.Input.Prompt, state.Model, ct);
            run.Phase = "plan";
            if (state.ExpectedSpeechLanguage == "unknown")
                Ask(run, "speech-intent", "The story requests speech, but its language is unclear. Choose English, no speech, or enter the intended language and optional dialogue.",
                    [new("english", "English"), new("none", "No speech"), new("correct", "Use the language I enter")]);
            return "The Audio Engineer checked the intended spoken language.";
        }
        if (run.Phase == "initialize")
        {
            if (stories == null) throw new InvalidOperationException("Story creation is not configured.");
            var story = await stories.EnsureAsync(run.Id, state.StoryName!, ct);
            var created = await ReadMemory(story.WorkspaceId, ct);
            if (created.Connection?.StoryId != story.StoryId || created.Entities.Count != 0 || created.History.Count != 0 || created.Prompts?.Count > 0)
                throw new InvalidOperationException("The new story is not empty or belongs to another connection. No clips were submitted.");
            run.WorkspaceId = created.Id;
            state.Input = state.Input with { WorkspaceId = created.Id };
            state.StoryId = story.StoryId; state.StoryName = created.Name;
            state.WorkspaceRevision = ScenePromptReducer.Revision(created);
            run.Phase = state.ProductionEnabled ? "project" : "plan";
            return "Created a new story with fresh Scene Memory. Planning your sequence next.";
        }
        if (run.Phase == "project")
        {
            var project = await production!.EnsureProjectAsync(run.Id, state.StoryId!, state.Input.StoryMode!, state.StoryName!, ct, state.Input.ProjectId);
            if (state.Input.ProjectId != null && project.ProjectId != state.Input.ProjectId)
                throw new InvalidOperationException("The studio returned a different project for this sequence.");
            if (string.IsNullOrWhiteSpace(project.ProjectId)) throw new InvalidOperationException("Missing project identity.");
            state.ProjectId = project.ProjectId; state.PreviousContinuousMusic = project.ContinuousMusic; run.Phase = state.SpeechReviewEnabled ? "speech-intent" : "plan";
            return "Your story’s project is ready. Other projects are unchanged.";
        }
        if (run.Phase == "finish")
        {
            if (state.Clips.Any(c => !c.Accepted || c.VideoUrl == null)) throw new InvalidOperationException("Unapproved clips cannot be assembled.");
            var keys = state.Clips.Select((c, i) => $"{run.Id}-{i + 1}-{c.RenderAttempt}").ToArray();
            var result = state.AudioEnabled
                ? await production!.FinishAudioAsync(run.Id, state.ProjectId!, keys, state.AudioChoice ?? throw new InvalidOperationException("Audio approval is required."), ct)
                : await production!.FinishAsync(run.Id, state.ProjectId!, keys, ct);
            if (!HttpSceneVideoRenderer.ValidArtifactUrl(result.VideoUrl)) throw new InvalidOperationException("Missing final video.");
            state.FinalVideoUrl = result.VideoUrl; run.Status = "completed";
            return "Your finished video is ready to watch and download.";
        }
        if (run.Phase == "plan-review")
        {
            if (await UnchangedAsync(run, state, ct) == null) return "Scene Memory changed.";
            var previewClip = state.Clips[state.ReviewIndex];
            var virtualWorkspace = state.ReviewWorkspace!;
            var scoped = SceneVideoMemoryScope.Select(virtualWorkspace, state, state.ReviewIndex);
            var extraction = await interpreter.InterpretAsync(scoped, previewClip.Prompt, state.Model, ct);
            if (extraction.Questions.Count > 0)
            {
                Ask(run, "planning", string.Join("\n", extraction.Questions).Truncate(4000), [new("explain", "Clarify the plan")]);
                run.Phase = "plan"; return "The plan needs clarification before rendering.";
            }
            var preview = new ScenePromptPreview(previewClip.ShotId, ScenePromptReducer.Revision(virtualWorkspace), previewClip.Prompt,
                $"Clip {state.ReviewIndex + 1}/{state.Clips.Count}", state.Model, extraction, "Video sequence", previewClip.DurationSeconds);
            SceneVideoMemoryScope.Validate(virtualWorkspace, scoped, preview);
            virtualWorkspace = ScenePromptReducer.Apply(virtualWorkspace, preview);
            previewClip.Preview = preview;
            var participants = SceneVideoPromptBuilder.Participants(virtualWorkspace, previewClip.ShotId);
            previewClip.PlanDetails = new(participants.Select(e => new ScenePlanEntity(e.Name, e.Kind)).ToArray(),
                participants.Where(e => e.Studio != null).Select(e => new ScenePlanReference(e.Studio!.DisplayName, e.Studio.AssetId,
                    Enumerable.Range(1, 3).Select(i => e.Studio.Defaults.GetValueOrDefault($"cynelo_reference_image_{i}")).Where(id => !string.IsNullOrWhiteSpace(id)).Cast<string>().ToArray(),
                    e.Studio.Defaults.GetValueOrDefault("cynelo_image_asset_id"))).ToArray());
            state.ReviewWorkspace = virtualWorkspace;
            if (++state.ReviewIndex == state.Clips.Count)
            {
                state.PlannedEntities = virtualWorkspace.Entities; state.ReviewWorkspace = null; run.Phase = "prepare";
                if ((state.Input.ReviewPlan || Dedicated(state)) && !state.PlanReviewed)
                    Ask(run, "review", Dedicated(state) ? "Review the shots and exact narration below. One consistent English voice will replace the video’s speech. Approve or describe changes before rendering." : "Review each clip’s action, cast and references. Approve or describe changes before rendering.", [new("approve", "Approve the plan"), new("revise", "Revise the plan")]);
            }
            return "Preparing your plan for review. No video has been submitted.";
        }
        if (run.Phase == "plan")
        {
            var workspace = await UnchangedAsync(run, state, ct);
            if (workspace == null) return "Scene Memory changed.";
            var durations = new List<int>();
            for (var left = state.Input.TotalSeconds; left > 0; left -= state.Input.ClipSeconds)
                durations.Add(Math.Min(left, state.Input.ClipSeconds));
            var plan = await planner.PlanAsync(SceneVideoMemoryScope.Select(workspace, state), state, durations, ct);
            if (!string.IsNullOrWhiteSpace(plan.Question))
            {
                Ask(run, "planning", plan.Question, (plan.Choices ?? []).Select((v, i) => new AgentChoice(i.ToString(), v)).ToArray());
                return "The planner needs your input.";
            }
            if (plan.Clips.Count != durations.Count || plan.Clips.Any(string.IsNullOrWhiteSpace))
                throw new InvalidOperationException("Planner did not return one prompt per clip.");
            state.Clips = plan.Clips.Select((p, i) => new SceneVideoClip { ShotId = $"{run.Id}-{i + 1}", Prompt = p, DurationSeconds = durations[i] }).ToList();
            state.ReviewIndex = 0; state.ReviewWorkspace = state.ProductionEnabled ? workspace : null;
            state.PlannedEntities = [];
            run.Phase = state.ProductionEnabled ? (Dedicated(state) ? "narration-plan" : "plan-review") : "prepare";
            if (!state.ProductionEnabled && state.Input.ReviewPlan && !state.PlanReviewed)
                Ask(run, "review", "The clip plan is ready. Approve it or describe what should change.", [new("approve", "Approve the plan"), new("revise", "Revise the plan")]);
            return $"Planned {state.Clips.Count} consecutive clips covering {state.Input.TotalSeconds} seconds.";
        }
        var clip = state.Clips[state.Current];
        var label = $"Clip {state.Current + 1}/{state.Clips.Count}";
        switch (run.Phase)
        {
            case "critic":
            {
                clip.ChecksVersion = 1;
                try {
                    var evidence = await production!.EvidenceAsync(RenderKey(run, state), ct);
                    var previous = state.Current == 0 ? null : await production.EvidenceAsync(
                        $"{run.Id}-{state.Current}-{state.Clips[state.Current - 1].RenderAttempt}", ct);
                    clip.Continuity = await critic!.CheckAsync(clip.VideoPrompt!, evidence, previous, ct);
                } catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) {
                    log.LogWarning(ex, "Visual inspection unavailable for {RunId}", run.Id);
                    clip.Continuity = new("uncertain", "Automatic visual review was unavailable.", []);
                }
                clip.VisualAccepted = clip.Continuity.Status == "passed";
                if (state.SpeechReviewEnabled) {
                    run.Phase = "speech-check";
                    return "Visual inspection is saved. Checking speech independently before asking for review.";
                }
                return ReviewChecks(run, state, label);
            }
            case "speech-check":
            {
                await InspectSpeech(run, state, ct);
                return ReviewChecks(run, state, label);
            }
            case "prepare":
            {
                var workspace = await UnchangedAsync(run, state, ct);
                if (workspace == null) return "Scene Memory changed.";
                var prompt = clip.Prompt + (string.IsNullOrWhiteSpace(state.Clarifications) ? "" : "\nCreator clarifications: " + state.Clarifications);
                var scoped = SceneVideoMemoryScope.Select(workspace, state);
                var extraction = state.ProductionEnabled && clip.Preview != null ? Rebase(clip.Preview.Extraction, state.PlannedEntities, workspace)
                    : await interpreter.InterpretAsync(scoped, prompt, state.Model, ct);
                if (extraction.Questions.Count > 0)
                {
                    Ask(run, "clarification", string.Join("\n", extraction.Questions).Truncate(4000),
                        [new("preserve", "Preserve the established scene details"), new("explain", "I’ll explain the intended change")]);
                    return $"{label}: Scene Memory needs clarification.";
                }
                clip.Preview = new(clip.ShotId, state.WorkspaceRevision, prompt, label, state.Model, extraction,
                    "Video sequence", clip.DurationSeconds);
                clip.BeforePromptsWereNull = workspace.Prompts == null;
                // Validate the retrieval boundary as well as the complete workspace before checkpointing.
                SceneVideoMemoryScope.Validate(workspace, scoped, clip.Preview);
                ScenePromptReducer.Apply(workspace, clip.Preview);
                run.Phase = "commit"; clip.Status = "prepared";
                return $"{label}: checked the action against Scene Memory.";
            }
            case "commit":
            {
                var workspace = await ReadMemory(run.WorkspaceId, ct);
                // A crash after ApplyPrompt is safe: an existing shot proves this exact command was already committed.
                var recorded = workspace.Prompts?.Any(p => p.Id == clip.ShotId) == true;
                if (recorded)
                {
                    var shot = workspace.Prompts!.Single(p => p.Id == clip.ShotId);
                    var introduced = (shot.IntroducedEntityIds ?? []).ToHashSet(StringComparer.Ordinal);
                    var before = workspace with {
                        Entities = workspace.Entities.Where(e => !introduced.Contains(e.Id)).ToArray(),
                        History = workspace.History.Where(f => f.Sequence < shot.FirstSequence).ToArray(),
                        Prompts = clip.BeforePromptsWereNull ? null : workspace.Prompts.Where(p => p.Id != clip.ShotId).ToArray()
                    };
                    if (shot.Prompt != clip.Preview!.Prompt || shot.Label != clip.Preview.Label ||
                        shot.SceneLabel != clip.Preview.SceneLabel || shot.DurationSeconds != clip.Preview.DurationSeconds ||
                        shot.Model != clip.Preview.Model ||
                        JsonSerializer.Serialize(SceneShotDesigns.Capture(workspace.Entities)) != JsonSerializer.Serialize(shot.DesignSnapshots) ||
                        workspace.Prompts.Last().Id != clip.ShotId ||
                        workspace.History.Any(f => f.Sequence > shot.LastSequence) || ScenePromptReducer.Revision(before) != clip.Preview.Revision)
                    { AskConflict(run); return "Scene Memory changed after the interrupted commit."; }
                }
                if (!recorded && ScenePromptReducer.Revision(workspace) != state.WorkspaceRevision)
                { AskConflict(run); return "Scene Memory changed before the shot could be saved."; }
                if (!recorded) workspace = await SceneVideoTrace.InvokeAsync("Scene Memory", "memory", "Recorded facts", "Save approved shot", () => memory.ApplyPromptAsync(run.WorkspaceId, clip.Preview!, ct));
                state.WorkspaceRevision = ScenePromptReducer.Revision(workspace);
                clip.VideoPrompt = SceneVideoPromptBuilder.Prepare(workspace, clip.ShotId, state.WorkspaceRevision).Prompt;
                run.Phase = "submit"; clip.Status = "ready";
                return $"{label}: saved the shot and prepared its continuity-aware render request.";
            }
            case "submit":
            {
                if (!renderer.IsConfigured)
                { Ask(run, "retry", "Cynelo rendering is not connected. Configure its rendering endpoint, then continue.", [new("retry", "Connection ready — continue")]); return "Waiting for Cynelo."; }
                if (await UnchangedAsync(run, state, ct) == null) return "Scene Memory changed before rendering.";
                var job = await renderer.EnsureAsync(RenderKey(run, state), new(run.Id, run.WorkspaceId, clip.ShotId,
                    state.Current + 1, state.Clips.Count, clip.DurationSeconds, clip.VideoPrompt!,
                    state.Current > 0 ? state.Clips[state.Current - 1].LastFrameUrl : null,
                    state.StoryId, state.WorkspaceRevision,
                    state.Current > 0 ? $"{run.Id}-{state.Current}-{state.Clips[state.Current - 1].RenderAttempt}" : null,
                    state.Input.StoryMode ?? (state.Input.WorkspaceId == null ? "new" : "continue"), state.ProjectId, clip.ReplacesJobKey,
                    state.SpeechReviewEnabled ? SpeechDirection(state, clip) : null), ct);
                clip.SubmittedAt ??= DateTimeOffset.UtcNow; clip.ProviderJobId = job.Id;
                run.Phase = "poll"; clip.Status = "rendering";
                return AcceptJob(run, state, job, label);
            }
            case "poll":
            {
                // Reconcile completion before applying a waiting deadline: elapsed time does not imply the job is unfinished.
                var job = await CheckJobAsync(run, state, ct);
                if (job.Status is "queued" or "running" && DateTimeOffset.UtcNow - (clip.WaitExtendedAt ?? clip.SubmittedAt)
                    > TimeSpan.FromMinutes(Math.Clamp(options.CurrentValue.RenderTimeoutMinutes, 1, 240)))
                {
                    Ask(run, "timeout", $"{label}: Cynelo is taking longer than expected. Keep waiting for the same job? Status checks continue while you decide.", [new("wait", "Keep waiting")]);
                    ScheduleObservation(run);
                    return "Render time limit reached.";
                }
                return AcceptJob(run, state, job, label);
            }
            default: throw new InvalidOperationException("Unknown scene-video phase.");
        }
    }
    private void ScheduleObservation(AgentRun run) => run.DueAt = DateTimeOffset.UtcNow
        .AddSeconds(Math.Clamp(options.CurrentValue.WaitingPollSeconds, 5, 300));

    private async Task<SceneRenderJob> CheckJobAsync(AgentRun run, SceneVideoState state, CancellationToken ct)
    {
        var clip = state.Clips[state.Current];
        clip.LastCheckedAt = DateTimeOffset.UtcNow;
        clip.LastCheckSucceeded = false;
        var job = await renderer.GetAsync(RenderKey(run, state), ct);
        if (job.Id != clip.ProviderJobId) throw new InvalidOperationException("Cynelo returned a different job for the same key.");
        if (job.Status is not ("queued" or "running" or "completed" or "failed")) throw new InvalidOperationException("Invalid render status.");
        if (job.Status == "completed" && (!HttpSceneVideoRenderer.ValidArtifactUrl(job.VideoUrl)
            || (state.Current < state.Clips.Count - 1 && !HttpSceneVideoRenderer.ValidArtifactUrl(job.LastFrameUrl))))
            throw new InvalidOperationException("Completed render is missing a video or continuation frame.");
        clip.LastCheckSucceeded = true; clip.RendererStatus = job.Status;
        clip.Progress = NormalizeProgress(job);
        if (job.References != null) clip.References = job.References;
        return job;
    }

    private async Task ObserveWaitingAsync(AgentRunLease lease, SceneVideoState state, CancellationToken ct)
    {
        var run = lease.Run;
        if (!WaitMonitors.Any(m => m.Workflow == run.Workflow && m.Phase == run.Phase && m.QuestionKind == run.Question?.Kind))
            throw new InvalidOperationException("This human decision cannot be monitored automatically.");
        var clip = state.Clips[state.Current];
        var label = $"Clip {state.Current + 1}/{state.Clips.Count}";
        var observer = run.Question?.Kind == "continuity" ? "Audio Engineer" : "Render agent";
        var observedPhase = run.Phase; var exchangeId = Guid.NewGuid().ToString("N");
        await runs.ActivityAsync(lease, "Director", "Refresh the existing job or inspection while waiting.", ct, new("Director", observer, observedPhase, exchangeId));
        if (run.Question?.Kind == "continuity") {
            run.Phase = "review-wait"; // One observation for old checkpoints, never a recurring review poll.
            if (state.SpeechReviewEnabled && clip.Speech == null) {
                await InspectSpeech(run, state, ct);
                run.State = JsonSerializer.SerializeToElement(state);
                await runs.SaveAsync(lease, "Audio Engineer", "observation", "Visual approval is still pending. " + clip.Speech!.Summary, ct, new(observer, "Director", observedPhase, exchangeId));
            } else {
                ScheduleObservation(run);
                await runs.SaveAsync(lease, "Audio Engineer", "observation", run.Summary, ct, new(observer, "Director", observedPhase, exchangeId));
            }
            return;
        }
        string summary;
        try
        {
            var job = await CheckJobAsync(run, state, ct);
            if (job.Status == "completed")
            {
                run.Question = null;
                if (state.ProductionEnabled) run.Status = "running";
                summary = AcceptJob(run, state, job, label);
                if (!run.IsTerminal && !state.ProductionEnabled)
                {
                    summary = $"{label} finished while waiting for your input. The video is ready. Continue with the next clip?";
                    Ask(run, "continue", summary, [new("continue", "Continue with the next clip"), new("cancel", "Stop here")]);
                }
            }
            else if (job.Status == "failed")
            {
                clip.Status = "failed";
                summary = $"{label}: Cynelo reported a failed render while waiting. Start a new attempt?";
                Ask(run, "render-failed", summary, [new("retry", "Render this clip again"), new("cancel", "Stop here")]);
            }
            else if (job.Status is "running" or "queued" && run.Question?.Kind == "retry")
            {
                // The operational problem is resolved by evidence from the existing job.
                // This does not approve a plan, create another attempt, or bypass a timeout decision.
                run.Status = "running"; run.Question = null; run.Failures = 0;
                clip.WaitExtendedAt = DateTimeOffset.UtcNow;
                summary = $"{label}: Tracking recovered. Continuing the existing Cynelo job; no duplicate render was submitted.{ProgressSummary(clip)}";
            }
            else if (job.Status is "running" or "queued")
                summary = $"{label}: Cynelo is still {job.Status}.{ProgressSummary(clip)} Checking automatically while waiting for your input; no new clips will start.";
            else throw new InvalidOperationException("Invalid render status.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Could not observe rendering for waiting run {RunId}", run.Id);
            summary = ex is SceneRenderInterventionException
                ? $"{label}: {ex.Message} Status checks continue; no new render was submitted."
                : $"{label}: Could not refresh Cynelo's status. The last known result is preserved. Status checks will retry automatically; no new render was submitted.";
        }
        ScheduleObservation(run);
        run.State = JsonSerializer.SerializeToElement(state);
        await runs.SaveAsync(lease, "Render agent", "observation", summary, ct, new(observer, "Director", observedPhase, exchangeId));
    }

    private string AcceptJob(AgentRun run, SceneVideoState state, SceneRenderJob job, string label)
    {
        var clip = state.Clips[state.Current];
        clip.RendererStatus = job.Status; clip.LastCheckedAt = DateTimeOffset.UtcNow; clip.LastCheckSucceeded = true;
        clip.Progress = NormalizeProgress(job);
        if (job.References != null) clip.References = job.References;
        if (job.Status == "failed")
        {
            if (clip.RenderAttempt >= state.MaxAttempts)
                Ask(run, "render-failed", $"{label}: Cynelo reported a failed render after {clip.RenderAttempt} attempts. Start a new attempt?", [new("retry", "Render this clip again")]);
            else { clip.RenderAttempt++; ResetRender(clip); run.Phase = "submit"; }
            return $"{label}: Cynelo reported a failed render.";
        }
        if (job.Status != "completed")
        {
            if (job.Status is not ("queued" or "running")) throw new InvalidOperationException("Invalid render status.");
            run.DueAt = DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(options.CurrentValue.PollSeconds, 1, 60));
            var selection = clip.References is { } refs
                ? $" Using {refs.Selected} of {refs.Available} reference images across {refs.Designs} designs." : "";
            return $"{label}: Cynelo is {job.Status}.{ProgressSummary(clip)}{selection} {state.Current} clips are ready.";
        }
        if (!HttpSceneVideoRenderer.ValidArtifactUrl(job.VideoUrl) ||
            (state.Current < state.Clips.Count - 1 && !HttpSceneVideoRenderer.ValidArtifactUrl(job.LastFrameUrl)))
            throw new InvalidOperationException("Completed render is missing a video or continuation frame.");
        clip.VideoUrl = job.VideoUrl; clip.LastFrameUrl = job.LastFrameUrl; clip.Status = "completed";
        if (state.ProductionEnabled) { run.Phase = "critic"; return $"{label} is saved. Checking visual continuity."; }
        return AdvanceClip(run, state, label);
    }
    private async Task<string> IdentifySpeechLanguage(string prompt, string model, CancellationToken ct)
    {
        try { return await audio!.SpeechLanguageAsync(prompt, model, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or OperationCanceledException or KeyNotFoundException) { return "unknown"; }
    }
    private static bool Dedicated(SceneVideoState state) => state.Input.NarrationMode == "dedicated";
    private async Task InspectSpeech(AgentRun run, SceneVideoState state, CancellationToken ct)
    {
        var clip = state.Clips[state.Current];
        if (clip.Speech != null) return; // Persisted evidence is reused after actor restarts and human decisions.
        try {
            clip.Speech = await production!.InspectSpeechAsync(RenderKey(run, state), ct);
            var heard = clip.Speech;
            var matches = heard.Status == "detected" && heard.Confidence >= .85 && heard.Language == state.ExpectedSpeechLanguage;
            if (matches && !Dedicated(state) && !await audio!.SpeechUnderstoodAsync(clip.Prompt + "\n" + clip.SpeechInstruction, state.Model, heard, ct)) {
                matches = false;
                clip.Speech = heard with { Status = "uncertain", Summary = "The detected language matches, but the Audio Engineer could not confidently understand or verify the words." };
            }
            clip.SpeechAccepted = Dedicated(state) || matches || (heard.Status == "no-speech" && state.ExpectedSpeechLanguage == "none");
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) {
            log.LogWarning(ex, "Speech inspection unavailable for {RunId}", run.Id);
            clip.Speech = new("unavailable", null, 0, "", "Speech inspection was unavailable. Listen before deciding.");
            clip.SpeechAccepted = Dedicated(state); // Native speech is replaced; the dedicated track has its own verification.
        }
    }
    private static string ReviewChecks(AgentRun run, SceneVideoState state, string label)
    {
        var clip = state.Clips[state.Current];
        if (!clip.VisualAccepted) {
            run.Phase = "review-wait";
            Ask(run, "continuity", (clip.Continuity?.Summary ?? "Visual review needs your attention.") + " Review the saved clip. " +
                (Dedicated(state) ? "Its speech will be replaced with your approved narration." : clip.Speech?.Summary ?? ""),
                [new("accept", "Keep these visuals"), new("cancel", "Stop here")]);
            return "Both checks are saved; visual approval is needed.";
        }
        if (state.SpeechReviewEnabled && clip.Speech == null) { run.Phase = "speech-check"; return "Checking speech and language."; }
        if (!state.SpeechReviewEnabled || clip.SpeechAccepted) return AdvanceClip(run, state, label);
        var choices = new List<AgentChoice> { new("accept", "Keep this clip"), new("recheck", "Check speech again · no new render") };
        if (clip.SpeechRetries < 2) {
            choices.Add(new("english", "Regenerate with English speech"));
            choices.Add(new("none", "Regenerate without speech"));
            choices.Add(new("correct", "Regenerate with my language / dialogue"));
        }
        if (state.AudioEnabled) choices.Add(new("narration", "Replace with narration · keep video"));
        choices.Add(new("cancel", "Stop here"));
        Ask(run, "speech-review", $"{label}: {clip.Speech!.Summary} Expected: {LanguageName(state.ExpectedSpeechLanguage)}. Listen before deciding. Regenerating creates a new video take and may change its visuals. Enter a language and optional exact dialogue for a correction.", choices);
        return "Speech needs your decision.";
    }
    private static string ApproveVisual(AgentRun run, SceneVideoState state, string label)
    {
        state.Clips[state.Current].VisualAccepted = true;
        return ReviewChecks(run, state, label);
    }
    private static string LanguageName(string code)
    {
        if (code == "none") return "no speech";
        if (code is "auto" or "unknown") return "not specified";
        try { return System.Globalization.CultureInfo.GetCultureInfo(code).EnglishName; }
        catch (System.Globalization.CultureNotFoundException) { return code; }
    }
    private static string SpeechDirection(SceneVideoState state, SceneVideoClip clip) =>
        (Dedicated(state) ? "Off-screen narration will be added separately. Do not generate human speech, dialogue, singing, or lip movements. Preserve only requested background sounds and instrumental music." : state.ExpectedSpeechLanguage == "none" ? "No dialogue, narration, or human speech." :
            "All human speech must be intelligible " + LanguageName(state.ExpectedSpeechLanguage) + ". Do not invent extra dialogue.") +
        (string.IsNullOrWhiteSpace(clip.SpeechInstruction) ? "" : " Creator's audio correction: " + clip.SpeechInstruction);
    private static string AdvanceClip(AgentRun run, SceneVideoState state, string label)
    {
        state.Clips[state.Current].Accepted = true;
        state.Current++;
        if (state.Current == state.Clips.Count)
        { if (state.ProductionEnabled) { run.Phase = state.AudioEnabled ? "audio-plan" : "finish"; run.Status = "running"; return state.AudioEnabled ? "All clips are approved. The Director is asking the Audio Engineer to prepare the story soundtrack." : "All clips are approved. Preparing your finished video."; }
          run.Status = "completed"; return $"All {state.Clips.Count} clips are ready ({state.Input.TotalSeconds} seconds)."; }
        run.Phase = "prepare";
        return $"{label} is ready. Continuing from its last frame into the next clip.";
    }
    private Task<SceneWorkspace> ReadMemory(string workspace, CancellationToken ct) =>
        SceneVideoTrace.InvokeAsync("Scene Memory", "memory", "Recorded facts", "Read story facts", () => memory.GetAsync(workspace, ct));
    private async Task<SceneWorkspace?> UnchangedAsync(AgentRun run, SceneVideoState state, CancellationToken ct)
    {
        var workspace = await ReadMemory(run.WorkspaceId, ct);
        if (ScenePromptReducer.Revision(workspace) == state.WorkspaceRevision) return workspace;
        AskConflict(run); return null;
    }
    private static void AskConflict(AgentRun run) => Ask(run, "conflict",
        "Scene Memory changed outside this run. Cancel this run and start a new sequence from the updated scene to avoid mixing continuity versions.",
        [new("cancel", "Cancel this run")]);
    private async Task<string> ResumeAsync(AgentRun run, SceneVideoState state, CancellationToken ct)
    {
        var reply = run.Reply!; var question = run.Question!;
        if (reply.Choice == "cancel") { run.Status = "cancelled"; run.Question = null; run.Reply = null; return "Cancelled at your request."; }
        if (question.Kind == "conflict")
        { run.Status = "waiting"; run.Reply = null; return question.Prompt; }
        var answer = question.Choices.FirstOrDefault(c => c.Id == reply.Choice)?.Label ?? "";
        if (!string.IsNullOrWhiteSpace(reply.Text)) answer += "\n" + reply.Text;
        state.HumanNotes.Add(answer);
        if (state.HumanNotes.Count > 100) state.HumanNotes.RemoveAt(0);
        if (question.Kind is "retry" or "timeout" or "render-failed" or "continue" or "continuity" or "audio-review" or "speech-review" or "speech-intent" or "narration-language" or "narration-review" && string.IsNullOrWhiteSpace(reply.Choice))
        {
            run.Status = "waiting"; run.Reply = null;
            const string note = " Your note was recorded. Choose the offered action to resume, or cancel to change the sequence.";
            run.Question = question with { Id = Guid.NewGuid().ToString("N"), Prompt = question.Prompt.Contains(note) ? question.Prompt : question.Prompt + note };
            return "Your note was recorded; the workflow is still paused.";
        }
        switch (question.Kind)
        {
            case "narration-review":
                if (reply.Choice == "approve" && string.IsNullOrWhiteSpace(reply.Text)) {
                    run.Phase = state.NarrationReturnPhase!; state.NarrationReturnPhase = null;
                    if (run.Phase == "review-wait") {
                        state.Clips[state.Current].SpeechAccepted = true;
                        ReviewChecks(run, state, $"Clip {state.Current + 1}/{state.Clips.Count}");
                    }
                } else {
                    state.Clarifications = (state.Clarifications + "\nNarration correction: " + answer).TakeLastText(12000);
                    run.Phase = "narration-plan";
                }
                break;
            case "narration-language":
                if (reply.Choice == "english") { state.ExpectedSpeechLanguage = "en"; state.Clarifications = (state.Clarifications + "\nCreator explicitly requests English narration; translate supplied narration to English for approval.").TakeLastText(12000); run.Phase = "narration-plan"; }
                else if (reply.Choice == "native") { state.Input = state.Input with { NarrationMode = "native" }; run.Phase = state.NarrationReturnPhase ?? "plan-review"; state.NarrationReturnPhase = null; if (run.Phase == "review-wait") ReviewChecks(run, state, $"Clip {state.Current + 1}/{state.Clips.Count}"); }
                else throw new InvalidOperationException("Choose a narration mode.");
                break;
            case "speech-intent": case "speech-review":
                if (question.Kind == "speech-review" && reply.Choice == "narration" && state.AudioEnabled) {
                    state.Input = state.Input with { NarrationMode = "dedicated" };
                    state.NarrationReturnPhase = "review-wait"; run.Phase = "narration-plan"; break;
                }
                if (question.Kind == "speech-review" && reply.Choice == "recheck") { state.Clips[state.Current].Speech = null; run.Phase = "speech-check"; break; }
                if (question.Kind == "speech-review" && reply.Choice == "accept") {
                    state.Clips[state.Current].SpeechAccepted = true;
                    ReviewChecks(run, state, $"Clip {state.Current + 1}/{state.Clips.Count}"); break;
                }
                var language = reply.Choice switch { "english" => "en", "none" => "none", "correct" when !string.IsNullOrWhiteSpace(reply.Text) =>
                    await IdentifySpeechLanguage("Human correction: " + reply.Text, state.Model, ct), _ => "unknown" };
                if (language is "unknown" or "auto") {
                    run.Status = "waiting"; run.Reply = null;
                    run.Question = question with { Id = Guid.NewGuid().ToString("N"), Prompt = "Enter the intended spoken language, for example Persian or Spanish, and optionally the exact dialogue. Then choose the correction action." };
                    return "The language is still unclear; no render was submitted.";
                }
                state.ExpectedSpeechLanguage = language;
                if (question.Kind == "speech-review") {
                    var target = state.Clips[state.Current];
                    if (target.SpeechRetries >= 2) throw new InvalidOperationException("Speech correction limit reached.");
                    target.ReplacesJobKey = RenderKey(run, state);
                    target.SpeechInstruction = reply.Text?.Truncate(2000);
                    target.SpeechRetries++; target.RenderAttempt++; ResetRender(target);
                    target.ChecksVersion = 1; target.Speech = null; target.Continuity = null; target.Accepted = false; target.VisualAccepted = false; target.SpeechAccepted = false;
                    target.VideoUrl = null; target.LastFrameUrl = null; target.Status = "ready"; run.Phase = "submit";
                } else { state.Clarifications = (state.Clarifications + "\nSpoken language: " + LanguageName(language) + ". " + reply.Text).TakeLastText(12000); run.Phase = "plan"; }
                break;
            case "audio-review":
                if (reply.Choice == "narration" && Dedicated(state)) {
                    state.Clarifications = (state.Clarifications + "\nNarration correction: " + answer).TakeLastText(12000);
                    state.NarrationReturnPhase = "audio-plan"; run.Phase = "narration-plan"; break;
                }
                if (reply.Choice == "retry") { run.Phase = "audio-process"; break; }
                if (reply.Choice != "original" && !(reply.Choice == "improved" && state.AudioDraft?.ImprovedAvailable == true))
                    throw new InvalidOperationException("Choose an available audio version.");
                state.AudioChoice = reply.Choice; run.Phase = "finish"; break;
            case "planning": case "clarification":
                state.Clarifications = (state.Clarifications + "\n" + answer).TakeLastText(12000); break;
            case "review":
                if (reply.Choice == "approve" && string.IsNullOrWhiteSpace(reply.Text)) state.PlanReviewed = true;
                else { state.Clarifications = (state.Clarifications + "\n" + answer).TakeLastText(12000); run.Phase = "plan"; }
                break;
            case "continuity":
                if (reply.Choice != "accept") throw new InvalidOperationException("Explicit acceptance is required.");
                ApproveVisual(run, state, $"Clip {state.Current + 1}/{state.Clips.Count}"); break;
            case "timeout": state.Clips[state.Current].WaitExtendedAt = DateTimeOffset.UtcNow; break;
            case "render-failed":
                state.Clips[state.Current].RenderAttempt++; ResetRender(state.Clips[state.Current]); run.Phase = "submit"; break;
        }
        if (run.Question?.Id == question.Id) run.Question = null;
        run.Reply = null;
        await Task.CompletedTask;
        return "Your answer was saved. Resuming from the last completed step.";
    }
    // Review is a dry run: provisional entity IDs must resolve to the committed identities by name and kind.
    private static ScenePromptExtraction Rebase(ScenePromptExtraction extraction, IReadOnlyList<SceneEntity> planned, SceneWorkspace actual)
    {
        string Id(string id) {
            var proposed = planned.FirstOrDefault(e => e.Id == id);
            return proposed == null ? id : actual.Entities.FirstOrDefault(e => e.Name == proposed.Name && e.Kind == proposed.Kind)?.Id ?? id;
        }
        return extraction with {
            Entities = extraction.Entities.Select(e => e with { Ref = Id(e.Ref) }).ToArray(),
            Changes = extraction.Changes.Select(c => c with { Subject = Id(c.Subject), Target = Id(c.Target) }).ToArray(),
            Events = extraction.Events?.Select(e => e with { Participants = e.Participants.Select(Id).ToArray() }).ToArray()
        };
    }
    private static void ResetRender(SceneVideoClip clip)
    {
        clip.SubmittedAt = null; clip.WaitExtendedAt = null; clip.ProviderJobId = null;
        clip.LastCheckedAt = null; clip.LastCheckSucceeded = null; clip.RendererStatus = null;
        clip.Progress = null;
    }
    private static SceneRenderProgress? NormalizeProgress(SceneRenderJob job) => job.Status == "completed"
        ? new(100, "Completed", "Video saved and ready.", job.Progress?.UpdatedAt, false)
        : job.Progress is { } progress ? progress with {
            Percent = progress.Percent is >= 0 ? Math.Min(progress.Percent.Value, 99) : null,
            Stage = (progress.Stage ?? "Rendering").Truncate(100), Detail = progress.Detail?.Truncate(500), Estimated = true
        } : null;
    private static string ProgressSummary(SceneVideoClip clip) => clip.Progress is { Percent: { } percent } progress
        ? $" Estimated progress: {percent}% — {progress.Stage}." : "";
    private static void Ask(AgentRun run, string kind, string prompt, IReadOnlyList<AgentChoice> choices)
    { run.Status = "waiting"; run.Question = new(Guid.NewGuid().ToString("N"), kind, prompt, choices); }
}

internal static class WorkflowText
{
    public static string Truncate(this string value, int length) => value.Length <= length ? value : value[..length];
    public static string TakeLastText(this string value, int length) => value.Length <= length ? value : value[^length..];
}
