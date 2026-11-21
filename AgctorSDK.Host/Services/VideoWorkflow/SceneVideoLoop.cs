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
        [new("scene-video", "plan-review", "retry"), new("scene-video", "submit", "retry"), new("scene-video", "poll", "timeout"), new("scene-video", "poll", "retry"), new("scene-video", "critic", "continuity"), new("scene-video", "speech-check", "continuity")];
    /// <summary>Mailbox owner for this run tick — same as <see cref="SceneVideoRoles.Owner"/> (Phase C).</summary>
    public static string Role(AgentRun run) => SceneVideoRoles.Owner(run);
    private static string DescribeStep(string phase) => phase switch {
        "plan" => "Plan the sequence.", "plan-review" => "Check the shot plan.",
        "project" => "Prepare the story’s project.", "initialize" => "Create the story’s memory.",
        "prepare" => "Check the next shot against Scene Memory.", "commit" => "Save the approved shot.",
        "reset-render" => "Retire the interrupted attempt while preserving saved clips.",
        "submit" => "Start the approved clip render.", "poll" => "Check the existing render’s progress.",
        "critic" => "Review visual continuity.", "speech-intent" => "Identify the requested spoken language.",
        "speech-check" => "Inspect the speech and language.", "narration-plan" => "Prepare narration for approval.",
        "clip-audio-plan" => "Plan this clip’s audio on the shared timeline.",
        "clip-audio-process" => "Prepare this clip’s improved audio.",
        "clip-audio-check" => "Check the prepared clip audio.",
        "clip-audio-decision" => "Request approval of this clip’s improved audio.",
        "audio-plan" => "Plan the story soundtrack.", "audio-process" => "Prepare the audio tracks.",
        "audio-review" => "Review the sound and speech evidence.", "audio-decision" => "Request an audio choice.",
        "finish" => "Assemble the approved film.", _ => "Continue the current step."
    };
    public static SceneVideoState State(AgentRun run) => run.State.Deserialize<SceneVideoState>()!;
    public static string RenderKey(AgentRun run, SceneVideoState state) => $"{run.Id}-{state.Current + 1}-{state.Clips[state.Current].RenderAttempt}";
    /// <summary>Continue prefers the playback-final previous clip (New mix) unless the caller asked for original.</summary>
    public static string ContinuityMedia(SceneVideoState state) =>
        state.Input.ContinuityMedia ?? (state.Input.StoryMode == "continue" ? "final" : "original");
    /// <summary>Film slot for this run clip when replace-at-slot was requested; otherwise null (append).</summary>
    public static int? FilmSlot(SceneVideoState state) =>
        state.Input.ReplaceClipIndex is { } slot ? slot + state.Current : null;
    public async Task StepAsync(AgentRunLease lease, CancellationToken ct)
    {
        var run = lease.Run; var state = State(run); var role = Role(run);
        // Trace role matches the mailbox owner (Director never owns audio tools).
        using var trace = SceneVideoTrace.Begin(runs, lease, role, log);
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
            role == "Director" ? null : new("Director", role, dispatchedPhase, exchangeId, Operation: DescribeStep(run.Phase), Outcome: "started"));
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
            log.LogWarning("Scene video run {RunId} paused for intervention during {Phase}: code={InterventionCode}",
                run.Id, run.Phase, ex.Code ?? "(none)");
            summary = ex.Message;
            Ask(run, "retry", summary, RecoveryChoices(run, state), ex.Code);
        }
        catch (ScenePromptInterpretationException ex) when (run.Phase == "plan-review")
        {
            log.LogWarning(ex, "Shot plan validation failed for {RunId}, clip {ClipNumber}", run.Id, state.ReviewIndex + 1);
            run.Failures++;
            state.PlanProblem = ScenePlanProblem.From(state.ReviewIndex, ex);
            summary = $"Clip {state.ReviewIndex + 1}: {FailureReason(ex)}. {state.PlanProblem.Stage}. " +
                string.Join(" ", state.PlanProblem.Issues) + " Checked shots and narration are preserved. Resume the failed check, start this shot’s planning over, or revise its instructions.";
            // The interpreter already exhausted its bounded repairs/splitting. Repeating that
            // entire cycle automatically with the same checkpoint is not recovery.
            Ask(run, "planning-retry", summary, PlanningChoices);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Scene video run {RunId} failed during {Phase}", run.Id, run.Phase);
            run.Failures++;
            // Never expose upstream response bodies, URLs or credentials in the public activity feed.
            var reason = FailureReason(ex);
            summary = $"{reason} (Attempt {run.Failures}/{state.MaxAttempts}.)";
            if (run.Failures >= state.MaxAttempts)
                Ask(run, "retry", summary + (run.Phase is "submit" or "poll" ? " Your saved work is preserved. Reconnect and retry, or start this clip over if recovery remains blocked." : " Your saved work is preserved. Retry this step when ready."), RecoveryChoices(run, state));
            else run.DueAt = DateTimeOffset.UtcNow.AddSeconds(Math.Min(30, Math.Pow(2, run.Failures)));
        }
        run.State = JsonSerializer.SerializeToElement(state);
        await runs.SaveAsync(lease, role, polling && run.Status == "running" ? "observation" : run.Status,
            run.Status == "waiting" ? run.Question?.Prompt ?? summary : summary, ct, role == "Director" ? null : new(role, "Director", dispatchedPhase, exchangeId));
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
        ScenePromptValidationException => "Scene Memory could not validate the saved shot’s identities or relationships",
        HttpRequestException => "The required service could not be reached or returned an error",
        TimeoutException or OperationCanceledException => "The current step took too long to respond",
        _ => "The current step could not be completed safely"
    };

    private async Task<string> AdvanceAsync(AgentRun run, SceneVideoState state, CancellationToken ct)
    {
        if (run.Phase != "poll" && ++run.Steps > 512)
        { run.Status = "failed"; return "The workflow reached its step limit. Completed clips remain available."; }
        if (run.Phase == "review-wait")
        {
            // review-wait only holds a decision under the Director mailbox. Return after
            // switching to narration-plan so the Audio Engineer mailbox runs the script step.
            if (state.NarrationReturnPhase != null)
            {
                run.Phase = "narration-plan";
                return "Returning to the narration script.";
            }
            return ReviewChecks(run, state, $"Clip {state.Current + 1}/{state.Clips.Count}");
        }
        var audioKeys = state.Clips.Select((c, i) => $"{run.Id}-{i + 1}-{c.RenderAttempt}").ToArray();
        if (run.Phase is "clip-audio-plan" or "audio-plan")
        {
            state.AudioPlan ??= await audio!.PlanAsync(System.Text.Json.JsonSerializer.Serialize(new { story = state.Input.Prompt, clarifications = state.Clarifications, previousContinuousMusic = state.PreviousContinuousMusic }), state.Model, ct);
            var approved = state.Clips.Select((c,i) => new { Clip = c, Number = i + 1 }).Where(c => c.Clip.ApprovedAudioVersion != null).Select(c => new SceneAudioApproval(c.Number, c.Clip.ApprovedAudioVersion!)).ToArray();
            state.AudioPlan = state.AudioPlan with { Mix = state.Mix, MusicProfile = state.MusicProfile == "close" ? null : state.MusicProfile, ContinuousMusic = state.MusicContinuity ?? state.AudioPlan.ContinuousMusic, Separator = state.Input.AudioModel ?? "roformer",
                BackgroundMusic = state.BackgroundMusic == "matching" ? "matching" : null,
                Narration = Dedicated(state) ? await NarrationPlanAsync(run, state, ct) : null,
                Approved = approved.Length == 0 ? null : approved };
            run.Phase = run.Phase == "clip-audio-plan" ? "clip-audio-process" : "audio-process";
            return "The audio plan is ready. Preparing a shared soundtrack; original clips are preserved.";
        }
        if (run.Phase == "clip-audio-process")
        {
            var previewClip = state.Clips[state.Current];
            previewClip.AudioPreview = await production!.PrepareClipAudioAsync(run.Id, state.ProjectId!, audioKeys.Take(state.Current + 1).ToArray(), state.AudioPlan!, ct,
                state.Input.ReplaceClipIndex, ContinuityMedia(state), state.Input.KeepLaterClips);
            if (previewClip.AudioPreview.ImprovedAvailable && string.IsNullOrWhiteSpace(previewClip.AudioPreview.PreviewVersion))
                throw new InvalidOperationException("Missing immutable audio preview version.");
            run.Phase = "clip-audio-check";
            return previewClip.AudioPreview.Summary;
        }
        if (run.Phase == "clip-audio-check")
        {
            var previewClip = state.Clips[state.Current];
            previewClip.AudioPreviewReview = await audio!.ReviewAsync(state.Input.Prompt, state.Model, previewClip.AudioPreview!, ct);
            run.Phase = "clip-audio-decision";
            return "The Audio Engineer checked this clip’s prepared mix. " + previewClip.AudioPreviewReview.Summary;
        }
        if (run.Phase == "clip-audio-decision")
        {
            var previewClip = state.Clips[state.Current];
            var choices = new List<AgentChoice>();
            if (previewClip.AudioPreview?.ImprovedAvailable == true) choices.Add(new("approve", "Approve improved audio & continue"));
            choices.Add(new("retry", "Retry audio only"));
            if (Dedicated(state) && previewClip.AudioPreview?.ImprovedAvailable == true) choices.Add(new("balance", "Adjust speech and music balance"));
            if (Dedicated(state)) { choices.Add(new("narration", "Revise narration · keep video")); choices.Add(new("music", "Change background music · keep narration")); }
            choices.Add(new("cancel", "Stop here"));
            Ask(run, "clip-audio-review", previewClip.AudioPreview?.ImprovedAvailable == true
                ? "Listen to this clip with its prepared speech and soundtrack. Approving saves this mix for the finished film. The next clip waits for your choice."
                : "This clip’s improved audio could not be prepared or verified. Your video is safe. Retry audio only or revise the narration.", choices);
            return "Waiting for your clip audio review.";
        }
        if (run.Phase == "audio-process")
        {
            state.AudioDraft = await production!.PrepareAudioAsync(run.Id, state.ProjectId!, audioKeys, state.AudioPlan!, ct,
                state.Input.ReplaceClipIndex, ContinuityMedia(state), state.Input.KeepLaterClips);
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
            if (Dedicated(state) && state.Clips.Any(c => c.ApprovedAudioVersion == null)) choices.Add(new("narration", "Revise narration · keep video"));
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
            // Replacement starts from the approved scene's words, not a fresh paraphrase.
            var originals = state.RestoreOriginalNarration
                ? state.Clips.Select(c => c.NarrationText ?? SceneSpeechTextReview.Expected(c)).ToArray() : null;
            IReadOnlyList<string> scripts = originals != null && originals.Any(s => !string.IsNullOrWhiteSpace(s))
                ? originals : await audio!.NarrationAsync(state.Input.Prompt + "\n" + state.Clarifications, state.Model, state.Clips, ct);
            state.RestoreOriginalNarration = false;
            if (scripts.Count != state.Clips.Count || scripts.All(string.IsNullOrWhiteSpace) || scripts.Any(s => s == null || s.Length > 1500)) throw new InvalidOperationException("Invalid narration script.");
            for (var i = 0; i < scripts.Count; i++)
                if (state.Clips[i].ApprovedAudioVersion == null) state.Clips[i].NarrationText = scripts[i];
            if (state.NarrationReturnPhase != null) {
                // Stay on narration-plan until the question exists. Moving to review-wait
                // first made every retry fail as an unknown phase.
                var models = await production!.NarrationModelsAsync(ct);
                var narrationChoices = new List<AgentChoice>();
                if (models.Contains("kokoro")) narrationChoices.Add(new("approve", "Approve · Kokoro local voice"));
                if (models.Contains("eleven_multilingual_v2")) narrationChoices.Add(new("approve-cloud", "Approve · ElevenLabs cloud voice (paid)"));
                narrationChoices.Add(new("revise", "Revise narration"));
                narrationChoices.Add(new("cancel", "Stop here"));
                run.Phase = state.NarrationReturnPhase;
                Ask(run, "narration-review", "Review the exact narration for each clip and choose a voice. Cloud narration sends these words to ElevenLabs and uses your configured account. Your video frames are kept.", narrationChoices);
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
            var project = await production!.EnsureProjectAsync(run.Id, state.StoryId!, state.Input.StoryMode!, state.StoryName!, ct,
                state.Input.ProjectId, state.Input.ReplaceClipIndex, ContinuityMedia(state), state.Input.KeepLaterClips);
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
                ? await production!.FinishAudioAsync(run.Id, state.ProjectId!, keys, state.AudioChoice ?? throw new InvalidOperationException("Audio approval is required."), ct,
                    state.Input.ReplaceClipIndex, ContinuityMedia(state), state.Input.KeepLaterClips)
                : await production!.FinishAsync(run.Id, state.ProjectId!, keys, ct,
                    state.Input.ReplaceClipIndex, ContinuityMedia(state), state.Input.KeepLaterClips);
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
            var extraction = await InterpretPlanAsync(scoped, previewClip.Prompt, state.Model, ct);
            if (extraction.Questions.Count > 0)
            {
                Ask(run, "planning", string.Join("\n", extraction.Questions).Truncate(4000), [new("explain", "Clarify the plan")]);
                run.Phase = "plan"; return "The plan needs clarification before rendering.";
            }
            var preview = new ScenePromptPreview(previewClip.ShotId, ScenePromptReducer.Revision(virtualWorkspace), previewClip.Prompt,
                $"Clip {state.ReviewIndex + 1}/{state.Clips.Count}", state.Model, extraction, "Video sequence", previewClip.DurationSeconds);
            SceneVideoMemoryScope.Validate(virtualWorkspace, scoped, preview);
            virtualWorkspace = ScenePromptReducer.Apply(virtualWorkspace, preview);
            previewClip.Preview = preview; state.PlanProblem = null;
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
            var prompts = plan.Clips.Select(p => state.SpeechReviewEnabled && !Dedicated(state)
                ? SceneNativeSpeech.Format(p, state.ExpectedSpeechLanguage) : p).ToArray();
            if (state.SpeechReviewEnabled && !Dedicated(state) && state.ExpectedSpeechLanguage != "none" &&
                SceneNativeSpeech.PlanConcern(state.Input.Prompt, state.Clarifications, prompts, durations, state.ExpectedSpeechLanguage) is { } concern) {
                Ask(run, "planning", concern, [new("revise", "Revise the dialogue")]);
                return "The spoken script needs clarification before rendering.";
            }
            state.Clips = prompts.Select((p, i) => new SceneVideoClip { ShotId = $"{run.Id}-{i + 1}", Prompt = p,
                DurationSeconds = durations[i], NativeSpeechVersion = 1 }).ToList();
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
                SceneVideoEvidence? evidence = null;
                try {
                    evidence = await production!.EvidenceAsync(RenderKey(run, state), ct);
                    var previous = state.Current == 0 ? null : await production.EvidenceAsync(
                        $"{run.Id}-{state.Current}-{state.Clips[state.Current - 1].RenderAttempt}", ct);
                    clip.Continuity = await critic!.CheckAsync(SceneBoundaryGuidance.RenderPrompt(state, clip), evidence, previous, ct);
                } catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) {
                    log.LogWarning(ex, "Visual inspection unavailable for {RunId}", run.Id);
                    clip.Continuity = new("uncertain", "Automatic visual review was unavailable.", []);
                }
                if (evidence?.Ending?.Valid() == true && clip.Continuity.Ending is { } ending && ending.Valid()) {
                    // A contradictory frame-difference summary cannot stay "observed". The model is not upgraded the other way.
                    if (evidence.EndingMotion is { Consistent: false }) ending = ending with { Status = "uncertain" };
                    clip.Boundary = new(clip.ShotId, RenderKey(run, state), DateTimeOffset.UtcNow,
                        clip.Continuity.Model ?? "configured-vision-model", evidence.Ending.Seconds, ending, evidence.DurationSeconds);
                    await SaveBoundaryAsync(run.WorkspaceId, clip.Boundary, ct);
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
                if (state.ProductionEnabled && state.Current > 0 && !clip.BoundaryBypass &&
                    !await PrepareBoundaryAsync(run, state, ct)) return "The previous clip’s ending needs review before continuing.";
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
            case "reset-render":
            {
                var reset = await renderer.ResetAsync(RenderKey(run, state), ct);
                if (reset.Status is "queued" or "running") return "The original clip is finishing its save. Checking it before starting another attempt.";
                if (reset.Status == "completed") {
                    clip.ProviderJobId = reset.Id;
                    return AcceptJob(run, state, reset, label);
                }
                if (reset.Status != "reset") throw new InvalidOperationException("Cynelo did not acknowledge the clip reset.");
                clip.RenderAttempt++; ResetRender(clip);
                clip.Status = "ready"; clip.VideoUrl = null; clip.LastFrameUrl = null;
                clip.Continuity = null; clip.Speech = null; clip.Accepted = false;
                clip.VisualAccepted = false; clip.SpeechAccepted = false; clip.SpeechVerified = null; clip.AudioDecision = null;
                run.Phase = "submit";
                return $"{label}: the interrupted attempt was retired. Starting this clip again; earlier clips are preserved.";
            }
            case "submit":
            {
                if (!renderer.IsConfigured)
                { Ask(run, "retry", "Cynelo rendering is not connected. Configure its rendering endpoint, then continue.", [new("retry", "Connection ready — continue")]); return "Waiting for Cynelo."; }
                if (await UnchangedAsync(run, state, ct) == null) return "Scene Memory changed before rendering.";
                var job = await renderer.EnsureAsync(RenderKey(run, state), new(run.Id, run.WorkspaceId, clip.ShotId,
                    state.Current + 1, state.Clips.Count, clip.DurationSeconds, SceneNativeSpeech.RenderPrompt(state, clip),
                    state.Current > 0 ? state.Clips[state.Current - 1].LastFrameUrl : null,
                    state.StoryId, state.WorkspaceRevision,
                    state.Current > 0 ? $"{run.Id}-{state.Current}-{state.Clips[state.Current - 1].RenderAttempt}" : null,
                    state.Input.StoryMode ?? (state.Input.WorkspaceId == null ? "new" : "continue"), state.ProjectId, clip.ReplacesJobKey,
                    state.SpeechReviewEnabled ? SpeechDirection(state, clip) : null, SceneBoundaryGuidance.Instruction(state, clip),
                    ContinuityMedia(state), FilmSlot(state), state.Input.KeepLaterClips), ct);
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
                    Ask(run, "timeout", $"{label}: Cynelo is taking longer than expected. Keep waiting for the same job? Status checks continue while you decide.", [new("wait", "Keep waiting"), new("restart", "Start this clip over · keep completed clips")]);
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
        // A lost submit response can leave the provider ID empty even though the stable request key exists.
        if (clip.ProviderJobId != null && job.Id != clip.ProviderJobId)
            throw new SceneRenderInterventionException("The saved attempt no longer matches the connected render. Reconnect to the original studio or start this clip over.", "job_identity_changed");
        clip.ProviderJobId ??= job.Id;
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
        if (run.Phase == "plan-review" && run.Question?.Kind == "retry") {
            Ask(run, "planning-retry", $"Clip {state.ReviewIndex + 1} could not finish its scene check. Earlier checked shots and narration are preserved. Resume the failed check, start this shot’s planning over, or revise its instructions.", PlanningChoices);
            await runs.SaveAsync(lease, "Planner", "waiting", run.Question!.Prompt, ct);
            return;
        }
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
            var choices = RecoveryChoices(run, state);
            var code = ex is SceneRenderInterventionException interventionEx ? interventionEx.Code : null;
            var prompt = ex is SceneRenderInterventionException intervention ? intervention.Message
                : $"{label}: The existing render could not be reached. Reconnect and retry, or start this clip over. Completed clips are preserved.";
            if (run.Question?.Kind == "retry" && (run.Question.Prompt != prompt || run.Question.Code != code || !run.Question.Choices.SequenceEqual(choices)))
                Ask(run, "retry", prompt, choices, code);
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
        // Speech-language assist belongs to the Audio Engineer (LLM), not the Director mailbox.
        try
        {
            using (SceneVideoTrace.SwitchRole(SceneVideoRoles.AudioEngineer))
                return await audio!.SpeechLanguageAsync(prompt, model, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or OperationCanceledException or KeyNotFoundException) { return "unknown"; }
    }
    private async Task AskBackground(AgentRun run, SceneVideoState state, string returnPhase, CancellationToken ct)
    {
        state.BackgroundReturnPhase = returnPhase;
        // Availability is an Audio Engineer tool assist — never attribute it to the Director.
        bool available;
        using (SceneVideoTrace.SwitchRole(SceneVideoRoles.AudioEngineer))
            available = await production!.MatchingMusicAvailableAsync(ct);
        var choices = new List<AgentChoice> { new("preserve", "Preserve original background music") };
        choices.Add(new("matching", available ? "Recreate a closely matching instrumental" : "Recreate matching instrumental · setup needed", available ? null : true));
        choices.Add(new("matching-clean", "Cleaner recreation · allow more variation", available ? null : true));
        choices.Add(new("cancel", "Stop here"));
        Ask(run, "narration-background", "Choose the background under your approved narration. Preserve keeps the original music as closely as voice removal allows. Recreate uses the actual background as an audio reference to retain its melody, rhythm, instruments and progression; you will compare the result before approval." +
            (available ? "" : " Matching music needs the reference-audio music service to be configured first."), choices);
    }

    private sealed record MixReply(double SpeechDb, double MusicDb, bool ContinuousMusic);

    private static bool Dedicated(SceneVideoState state) => state.Input.NarrationMode == "dedicated";
    private async Task InspectSpeech(AgentRun run, SceneVideoState state, CancellationToken ct)
    {
        var clip = state.Clips[state.Current];
        clip.ExpectedSpeechText = Dedicated(state) ? clip.NarrationText : SceneSpeechTextReview.Expected(clip);
        if (clip.Speech != null) return; // Persisted evidence is reused after actor restarts and human decisions.
        try {
            clip.Speech = SceneSpeechTiming.Normalize(await production!.InspectSpeechAsync(RenderKey(run, state), ct));
            await SaveSpeechMomentAsync(run, state, clip, ct);
            var heard = clip.Speech;
            var matches = heard.Status == "detected" && heard.Confidence >= .85 && heard.Language == state.ExpectedSpeechLanguage;
            var expectedWords = Dedicated(state) ? "" : SceneSpeechTextReview.Expected(clip);
            if (!string.IsNullOrWhiteSpace(heard.Transcript)) {
                var review = await SceneSpeechTextReview.Evaluate(audio!, clip.Prompt + "\n" + clip.SpeechInstruction,
                    expectedWords, state.Model, heard, ct);
                clip.Speech = SceneSpeechTiming.Normalize(heard with { Issues = review.Issues });
                if (expectedWords.Length > 0 && !SceneNativeSpeech.Matches(expectedWords, heard.Transcript)) {
                    matches = false;
                    clip.Speech = SceneSpeechTiming.Normalize(clip.Speech with { Status = "uncertain", Summary = "The recognized words do not match the requested dialogue. Listen to the clip and check the transcript before deciding." });
                } else if (review.Status != "passed") {
                    if (matches) clip.Speech = SceneSpeechTiming.Normalize(clip.Speech with { Status = "uncertain", Summary = "The detected language matches, but the Audio Engineer could not confidently understand or verify the words." });
                    matches = false;
                }
            }
            if (string.IsNullOrWhiteSpace(heard.Transcript)) matches = false;
            clip.SpeechVerified = !Dedicated(state) && (matches || (heard.Status == "no-speech" && state.ExpectedSpeechLanguage == "none"));
            clip.SpeechAccepted = clip.SpeechVerified == true;
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) {
            log.LogWarning(ex, "Speech inspection unavailable for {RunId}", run.Id);
            clip.Speech = SceneSpeechTiming.Normalize(new("unavailable", null, 0, "", "Speech inspection was unavailable. Listen before deciding."));
            clip.SpeechVerified = false; clip.SpeechAccepted = false; // Planned replacement is not verified audio.
        }
    }
    private static string ReviewChecks(AgentRun run, SceneVideoState state, string label)
    {
        var clip = state.Clips[state.Current];
        if (!clip.VisualAccepted) {
            run.Phase = "review-wait";
            Ask(run, "continuity", (clip.Continuity?.Summary ?? "Visual review needs your attention.") + " Review the saved clip. " +
                (Dedicated(state) ? "Its speech will be replaced with your approved narration." : clip.Speech?.Summary ?? ""),
                VisualChoices(state, clip));
            return "Both checks are saved; visual approval is needed.";
        }
        if (state.SpeechReviewEnabled && clip.Speech == null) { run.Phase = "speech-check"; return "Checking speech and language."; }
        if (Dedicated(state) && state.SpeechReviewEnabled) {
            clip.SpeechAccepted = false; // Also correct old checkpoints that treated deferred narration as a pass.
            if (state.AudioEnabled && state.ProductionEnabled) return AdvanceClip(run, state, label);
            if (!clip.AudioReviewRequired || clip.AudioDecision == "replace-with-narration") return AdvanceClip(run, state, label);
            var dedicated = new List<AgentChoice> {
                new("replace", "Replace speech with approved narration"), new("narration", "Review or revise the narration"),
                new("recheck", "Recheck original speech") };
            if (SceneSpeechTiming.OfferSkip(clip.Speech)) dedicated.Add(new("skip-timing", "Continue without word timing"));
            dedicated.Add(new("cancel", "Stop here"));
            Ask(run, "speech-review", $"{label}: Visuals are approved. You are hearing the original H3 audio. Separate narration is planned, but it has not been generated or verified yet. Confirm replacement to continue, review the narration script, or recheck the original speech. You will review the final narration before delivery.",
                dedicated);
            return "Visuals approved. Waiting for your audio decision; the next clip has not started.";
        }
        if (!state.SpeechReviewEnabled || (clip.SpeechAccepted && (!clip.AudioReviewRequired || clip.AudioDecision == "keep-original"))) return AdvanceClip(run, state, label);
        var choices = new List<AgentChoice> { new("accept", "Approve this audio"), new("recheck", "Check speech again · no new render") };
        // Untimed/unavailable clocks are durable until skip or a successful timing pass — do not only offer Retry.
        if (SceneSpeechTiming.OfferSkip(clip.Speech))
            choices.Add(new("skip-timing", "Continue without word timing"));
        if (clip.SpeechRetries < 2) {
            choices.Add(new("english", "Regenerate with English speech"));
            choices.Add(new("none", "Regenerate without speech"));
            choices.Add(new("correct", "Regenerate with my language / dialogue"));
        }
        if (state.AudioEnabled) choices.Add(new("narration", "Replace with narration · keep video"));
        choices.Add(new("cancel", "Stop here"));
        var timingNote = SceneSpeechTiming.OfferSkip(clip.Speech)
            ? " Word timing is unavailable; you can continue without it, or check speech again."
            : "";
        Ask(run, "speech-review", $"{label}: Visuals are approved. {clip.Speech!.Summary} Expected: {LanguageName(state.ExpectedSpeechLanguage)}. Listen before deciding.{timingNote} Regenerating creates a new video take and may change its visuals. Enter a language and optional exact dialogue for a correction.", choices);
        return "Speech needs your decision.";
    }
    private static IReadOnlyList<AgentChoice> VisualChoices(SceneVideoState state, SceneVideoClip clip)
    {
        var choices = new List<AgentChoice> {
            new("accept", state.SpeechReviewEnabled ? "Keep visuals · review audio next" : "Keep these visuals"),
            new("recheck", "Check the transition again · no new render") };
        if (clip.VisualRetries < 2) choices.Add(new("regenerate", "Regenerate this clip · keep earlier clips"));
        choices.Add(new("cancel", "Stop here")); return choices;
    }
    private async Task<bool> PrepareBoundaryAsync(AgentRun run, SceneVideoState state, CancellationToken ct)
    {
        var prior = state.Clips[state.Current - 1];
        var key = $"{run.Id}-{state.Current}-{prior.RenderAttempt}";
        if (prior.Boundary?.RenderKey != key) prior.Boundary = null;
        prior.Boundary ??= (await memory.ReadBoundariesAsync(run.WorkspaceId, prior.ShotId, key, ct)).FirstOrDefault();
        if (prior.Boundary == null) {
            SceneVideoEvidence? evidence = null; SceneContinuityReport? report = null;
            try {
                using (SceneVideoTrace.SwitchRole(SceneVideoRoles.VisualCritic)) {
                    evidence = await production!.EvidenceAsync(key, ct);
                    report = await critic!.CheckAsync(prior.VideoPrompt ?? prior.Prompt, evidence, null, ct);
                }
            } catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { log.LogWarning(ex, "Boundary inspection unavailable for {RunId}", run.Id); }
            if (evidence?.Ending?.Valid() == true && report?.Ending is { } ending && ending.Valid()) {
                if (evidence.EndingMotion is { Consistent: false }) ending = ending with { Status = "uncertain" };
                prior.Boundary = new(prior.ShotId, key, DateTimeOffset.UtcNow, report.Model ?? "configured-vision-model", evidence.Ending.Seconds, ending, evidence.DurationSeconds);
                await SaveBoundaryAsync(run.WorkspaceId, prior.Boundary, ct);
            }
        }
        if (prior.Boundary?.Ending is { Status: "observed" } observed && observed.Valid() && prior.Boundary is not null) return true;
        Ask(run, "boundary", "The previous clip’s ending movement could not be established from the sampled frames. Review it before starting the next clip. Continuing without a verified ending can repeat or reverse an action.",
            [new("recheck", "Check the ending again"), new("continue", "Continue using the plan · ending unverified"), new("cancel", "Stop here"), new("prompt", "Write a transition prompt")]);
        return false;
    }
    private static string ApproveVisual(AgentRun run, SceneVideoState state, string label)
    {
        state.Clips[state.Current].VisualAccepted = true;
        state.Clips[state.Current].AudioReviewRequired = state.SpeechReviewEnabled;
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
            "All human speech must be intelligible " + LanguageName(state.ExpectedSpeechLanguage) + ". Do not invent extra dialogue." +
            (clip.NativeSpeechVersion > 0 ? " " + SceneNativeSpeech.Direction : "")) +
        (string.IsNullOrWhiteSpace(clip.SpeechInstruction) ? "" : " Creator's audio correction: " + clip.SpeechInstruction);
    private static string AdvanceClip(AgentRun run, SceneVideoState state, string label)
    {
        var reviewed = state.Clips[state.Current];
        if (state.AudioEnabled && state.ProductionEnabled && !reviewed.KeepOriginalAudio &&
            (reviewed.ApprovedAudioVersion == null || reviewed.ApprovedAudioVersion != reviewed.AudioPreview?.PreviewVersion)) {
            run.Phase = "clip-audio-plan";
            return "Visuals approved. Preparing this clip’s improved audio for you to hear before continuing.";
        }
        if (reviewed.AudioReviewRequired && !reviewed.KeepOriginalAudio && !(Dedicated(state) ? reviewed.AudioDecision == "replace-with-narration" : reviewed.SpeechAccepted && reviewed.AudioDecision == "keep-original"))
            throw new InvalidOperationException("An explicit audio decision is still required.");
        state.Clips[state.Current].Accepted = true;
        state.Current++;
        if (state.Current == state.Clips.Count)
        {
            if (state.ProductionEnabled)
            {
                if (state.AudioEnabled && state.Clips.All(c => c.KeepOriginalAudio))
                {
                    state.AudioChoice = "original";
                    run.Phase = "finish";
                    run.Status = "running";
                    return "All clips are approved with original audio. Preparing your finished video.";
                }
                run.Phase = state.AudioEnabled ? "audio-plan" : "finish";
                run.Status = "running";
                return state.AudioEnabled ? "All clips are approved. The Director is asking the Audio Engineer to prepare the story soundtrack." : "All clips are approved. Preparing your finished video.";
            }
            run.Status = "completed"; return $"All {state.Clips.Count} clips are ready ({state.Input.TotalSeconds} seconds).";
        }
        run.Phase = "prepare";
        return $"{label} is ready. Continuing from its last frame into the next clip.";
    }
    private async Task<SceneWorkspace> ReadMemory(string workspace, CancellationToken ct)
    {
        // Revision / fact reads may happen from Render or other mailboxes; attribute to Memory when needed.
        if (SceneVideoTrace.CurrentRole != null &&
            !SceneVideoRoles.MayInvoke(SceneVideoTrace.CurrentRole, "Scene Memory"))
        {
            using (SceneVideoTrace.SwitchRole(SceneVideoRoles.Memory))
                return await SceneVideoTrace.InvokeAsync("Scene Memory", "memory", "Recorded facts", "Read story facts",
                    () => memory.GetAsync(workspace, ct));
        }
        return await SceneVideoTrace.InvokeAsync("Scene Memory", "memory", "Recorded facts", "Read story facts",
            () => memory.GetAsync(workspace, ct));
    }
    private async Task<SceneWorkspace?> UnchangedAsync(AgentRun run, SceneVideoState state, CancellationToken ct)
    {
        var workspace = await ReadMemory(run.WorkspaceId, ct);
        if (ScenePromptReducer.Revision(workspace) == state.WorkspaceRevision) return workspace;
        AskConflict(run); return null;
    }
    private async Task SaveBoundaryAsync(string workspaceId, SceneBoundaryObservation boundary, CancellationToken ct)
    {
        if (SceneVideoTrace.CurrentRole != null &&
            !SceneVideoRoles.MayInvoke(SceneVideoTrace.CurrentRole, "Scene Memory"))
        {
            using (SceneVideoTrace.SwitchRole(SceneVideoRoles.Memory))
                await SceneVideoTrace.InvokeAsync("Scene Memory", "memory", "Recorded facts", "Save ending boundary",
                    async () => { await memory.SaveBoundaryAsync(workspaceId, boundary, ct); return 0; });
            return;
        }
        await memory.SaveBoundaryAsync(workspaceId, boundary, ct);
    }
    /// <summary>Writes one confirmed dialogue moment from measured word times. Planned events and story facts stay as they are.</summary>
    private async Task SaveSpeechMomentAsync(AgentRun run, SceneVideoState state, SceneVideoClip clip, CancellationToken ct)
    {
        var words = clip.Speech?.Words;
        if (words is not { Count: > 0 } || string.IsNullOrWhiteSpace(clip.ShotId) || string.IsNullOrWhiteSpace(clip.Speech?.Transcript)) return;
        var start = words.Min(w => w.StartSeconds); var end = words.Max(w => w.EndSeconds);
        if (end <= start || !double.IsFinite(start) || !double.IsFinite(end)) return;
        var workspace = await ReadMemory(run.WorkspaceId, ct);
        if (workspace == null || ScenePromptReducer.Revision(workspace) != state.WorkspaceRevision) return;
        var id = $"{clip.ShotId}-spoken-{RenderKey(run, state)}";
        if (workspace.Prompts?.FirstOrDefault(p => p.Id == clip.ShotId)?.TimedEvents?.Any(e => e.Id == id) == true) return;
        var timing = new SceneTimeRange((long)Math.Round(start * 1000), (long)Math.Round(end * 1000));
        if (timing.End <= timing.Start) return;
        var dialogue = clip.Speech!.Transcript.Trim();
        if (dialogue.Length > 4000) dialogue = dialogue[..4000];
        var item = new SceneTimedEvent(id, "dialogue", "Measured spoken line", timing, [], "confirmed", RenderKey(run, state), dialogue);
        async Task<int> Save() { await memory.SaveTimedEventAsync(run.WorkspaceId, clip.ShotId, state.WorkspaceRevision, item, ct); return 0; }
        if (SceneVideoTrace.CurrentRole != null && !SceneVideoRoles.MayInvoke(SceneVideoTrace.CurrentRole, "Scene Memory"))
        {
            using (SceneVideoTrace.SwitchRole(SceneVideoRoles.Memory))
                await SceneVideoTrace.InvokeAsync("Scene Memory", "memory", "Recorded facts", "Save spoken moment", Save);
        }
        else await memory.SaveTimedEventAsync(run.WorkspaceId, clip.ShotId, state.WorkspaceRevision, item, ct);
        state.WorkspaceRevision = ScenePromptReducer.Revision(await ReadMemory(run.WorkspaceId, ct));
    }
    private async Task<SceneNarrationPlan> NarrationPlanAsync(AgentRun run, SceneVideoState state, CancellationToken ct)
    {
        var count = run.Phase == "clip-audio-plan" ? state.Current + 1 : state.Clips.Count;
        var clips = state.Clips.Take(count).ToArray();
        var workspace = await ReadMemory(run.WorkspaceId, ct);
        var windows = clips.Select(c =>
        {
            var planned = SceneNarrationPace.PlannedDialogue(workspace, c.ShotId);
            var confirmed = SceneNarrationPace.ConfirmedDialogue(workspace, c.ShotId, $"{c.ShotId}-{c.RenderAttempt}");
            return SceneNarrationPace.Window(c.Speech?.Words, planned ?? (c.Speech?.Words is { Count: > 0 } ? null : confirmed), state.NarrationPaceOverride);
        }).ToArray();
        return new(state.NarrationModel, state.NarrationModel == "kokoro" ? "af_heart" : "configured", "en",
            clips.Select(c => c.NarrationText ?? "").ToArray(), windows, state.NarrationSoftness, state.NarrationAccent);
    }
    private static void AskConflict(AgentRun run) => Ask(run, "conflict",
        "Scene Memory changed outside this run. Cancel this run and start a new sequence from the updated scene to avoid mixing continuity versions.",
        [new("cancel", "Cancel this run")]);
    private async Task<string> ResumeAsync(AgentRun run, SceneVideoState state, CancellationToken ct)
    {
        var reply = run.Reply!; var question = run.Question!;
        if (reply.Choice == "cancel") {
            // A boundary stop is only a stop. Typed words belong to the prompt choice.
            var keepWords = question.Kind != "boundary" && !string.IsNullOrWhiteSpace(reply.Text);
            if (keepWords) state.HumanNotes.Add(reply.Text!.Trim());
            run.Status = "cancelled"; run.Question = null; run.Reply = null;
            return keepWords ? "Cancelled at your request. " + reply.Text!.Trim().Truncate(500) : "Cancelled at your request.";
        }
        if (question.Kind == "boundary" && string.IsNullOrWhiteSpace(reply.Choice)) {
            run.Status = "waiting"; run.Reply = null;
            return "Choose an ending action. Words alone were not applied.";
        }
        if (question.Kind == "boundary" && reply.Choice == "prompt" && string.IsNullOrWhiteSpace(reply.Text)) {
            run.Status = "waiting"; run.Reply = null;
            return "Enter a transition prompt. An empty prompt was not applied.";
        }
        if (question.Kind == "conflict")
        { run.Status = "waiting"; run.Reply = null; return question.Prompt; }
        if (run.Phase == "plan-review" && question.Kind is "retry" or "planning-retry") {
            var failed = state.Clips[state.ReviewIndex];
            if (reply.Choice == "revise-shot" && (string.IsNullOrWhiteSpace(reply.Text) || reply.Text.Length > 6000)) {
                run.Reply = null; run.Status = "waiting";
                run.Question = question with { Id = Guid.NewGuid().ToString("N"), Choices = PlanningChoices,
                    Prompt = "Enter replacement instructions for this shot (up to 6,000 characters), then choose Revise this shot’s instructions. Earlier checked shots and narration are kept." };
                return "Waiting for revised shot instructions; no model or render was started.";
            }
            if (reply.Choice is "retry" or "reset-plan" or "revise-shot") {
                var scoped = SceneVideoMemoryScope.Select(state.ReviewWorkspace!, state, state.ReviewIndex);
                if (reply.Choice != "retry") await interpreter.ResetAsync(scoped, failed.Prompt, state.Model, ct);
                if (reply.Choice == "revise-shot") {
                    failed.Prompt = reply.Text!.Trim(); failed.Preview = null; failed.PlanDetails = null;
                    state.PlanReviewed = false;
                }
                state.PlanProblem = null;
            }
        }
        var answer = question.Choices.FirstOrDefault(c => c.Id == reply.Choice)?.Label ?? "";
        // Boundary words ride only with the prompt choice. The other ending actions stay plain.
        if ((question.Kind != "boundary" || reply.Choice == "prompt") && !string.IsNullOrWhiteSpace(reply.Text))
            answer += "\n" + reply.Text;
        state.HumanNotes.Add(answer);
        if (state.HumanNotes.Count > 100) state.HumanNotes.RemoveAt(0);
        if (question.Kind is "retry" or "planning-retry" or "timeout" or "render-failed" or "continue" or "continuity" or "audio-review" or "clip-audio-review" or "speech-review" or "speech-intent" or "narration-language" or "narration-review" or "narration-background" && string.IsNullOrWhiteSpace(reply.Choice))
        {
            run.Status = "waiting"; run.Reply = null;
            const string note = " Your note was recorded. Choose the offered action to resume, or cancel to change the sequence.";
            run.Question = question with { Id = Guid.NewGuid().ToString("N"), Prompt = question.Prompt.Contains(note) ? question.Prompt : question.Prompt + note };
            return "Your note was recorded; the workflow is still paused.";
        }
        if (reply.Choice == "restart" && question.Kind is "retry" or "timeout" &&
            run.Phase is "submit" or "poll" && state.Current < state.Clips.Count) {
            // Save this phase before the external reset. A lost response repeats one idempotent reset, not another render.
            run.Phase = "reset-render"; run.Question = null; run.Reply = null;
            return $"Restart requested for clip {state.Current + 1}. Earlier clips will be kept.";
        }
        switch (question.Kind)
        {
            case "narration-review":
                if (reply.Choice is "approve" or "approve-cloud" && SceneNarrationPace.TryVoiceSettings(reply.Text, out var softness, out var accent, out var paceOverride)) {
                    state.NarrationModel = reply.Choice == "approve-cloud" ? "eleven_multilingual_v2" : "kokoro";
                    state.NarrationSoftness = softness;
                    state.NarrationAccent = string.IsNullOrWhiteSpace(accent) ? null : accent.Trim();
                    state.NarrationPaceOverride = paceOverride;
                    await AskBackground(run, state, "narration-approved", ct);
                } else {
                    state.Clarifications = (state.Clarifications + "\nNarration correction: " + answer).TakeLastText(12000);
                    run.Phase = "narration-plan";
                }
                break;
            case "narration-background":
                if (reply.Choice is not ("preserve" or "matching" or "matching-clean")) throw new InvalidOperationException("Choose a background music option.");
                bool matchingReady;
                using (SceneVideoTrace.SwitchRole(SceneVideoRoles.AudioEngineer))
                    matchingReady = await production!.MatchingMusicAvailableAsync(ct);
                if ((reply.Choice is "matching" or "matching-clean") && !matchingReady) {
                    await AskBackground(run, state, state.BackgroundReturnPhase!, ct); break;
                }
                if (state.Clips.Any(c => c.ApprovedAudioVersion != null) && state.AudioPlan?.ContinuousMusic == true &&
                    ((reply.Choice == "preserve" ? "preserve" : "matching") != state.BackgroundMusic ||
                     (reply.Choice == "matching-clean" ? "clean" : "close") != state.MusicProfile))
                    throw new InvalidOperationException("The shared music theme is already approved. Keep this theme when continuing the story.");
                state.BackgroundMusic = reply.Choice == "preserve" ? "preserve" : "matching";
                state.MusicProfile = reply.Choice == "matching-clean" ? "clean" : "close";
                run.Phase = state.BackgroundReturnPhase!; state.BackgroundReturnPhase = null;
                if (run.Phase == "narration-approved") {
                    run.Phase = state.NarrationReturnPhase!; state.NarrationReturnPhase = null;
                    if (run.Phase == "review-wait") {
                        state.Clips[state.Current].AudioDecision = "replace-with-narration";
                        state.Clips[state.Current].SpeechAccepted = false;
                        ReviewChecks(run, state, $"Clip {state.Current + 1}/{state.Clips.Count}");
                    }
                }
                break;
            case "narration-language":
                if (reply.Choice == "english") { state.RestoreOriginalNarration = false; state.ExpectedSpeechLanguage = "en"; state.Clarifications = (state.Clarifications + "\nCreator explicitly requests English narration; translate supplied narration to English for approval.").TakeLastText(12000); run.Phase = "narration-plan"; }
                else if (reply.Choice == "native") { state.Input = state.Input with { NarrationMode = "native" }; run.Phase = state.NarrationReturnPhase ?? "plan-review"; state.NarrationReturnPhase = null; if (run.Phase == "review-wait") ReviewChecks(run, state, $"Clip {state.Current + 1}/{state.Clips.Count}"); }
                else throw new InvalidOperationException("Choose a narration mode.");
                break;
            case "speech-intent": case "speech-review":
                if (question.Kind == "speech-review" && reply.Choice == "replace" && Dedicated(state)) {
                    state.Clips[state.Current].AudioDecision = "replace-with-narration";
                    ReviewChecks(run, state, $"Clip {state.Current + 1}/{state.Clips.Count}"); break;
                }
                if (question.Kind == "speech-review" && reply.Choice == "narration" && state.AudioEnabled) {
                    state.Input = state.Input with { NarrationMode = "dedicated" };
                    state.RestoreOriginalNarration = true;
                    state.NarrationReturnPhase = "review-wait"; run.Phase = "narration-plan"; break;
                }
                if (question.Kind == "speech-review" && reply.Choice == "recheck") { state.Clips[state.Current].Speech = null; state.Clips[state.Current].SpeechAccepted = false; state.Clips[state.Current].SpeechVerified = null; state.Clips[state.Current].AudioDecision = null; run.Phase = "speech-check"; break; }
                // Durable skip: keep transcript evidence, drop clocks, do not re-ask for timing alone.
                // Cynelo accepts skip-timing, continue-untimed, or continue for the same advance.
                if (question.Kind == "speech-review" && SceneSpeechTiming.IsSkipChoice(reply.Choice)) {
                    var timed = state.Clips[state.Current];
                    timed.Speech = SceneSpeechTiming.MarkSkipped(timed.Speech);
                    timed.SpeechAccepted = true;
                    timed.AudioDecision = "keep-original";
                    ReviewChecks(run, state, $"Clip {state.Current + 1}/{state.Clips.Count}"); break;
                }
                if (question.Kind == "speech-review" && reply.Choice == "accept") {
                    state.Clips[state.Current].SpeechAccepted = true;
                    state.Clips[state.Current].AudioDecision = "keep-original";
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
                    target.SpeechRetries++; target.RenderAttempt++; target.NativeSpeechVersion = 1; ResetRender(target);
                    target.AudioDecision = null; target.AudioReviewRequired = false; target.SpeechVerified = null;
                    target.ChecksVersion = 1; target.Speech = null; target.Continuity = null; target.Accepted = false; target.VisualAccepted = false; target.SpeechAccepted = false;
                    target.VideoUrl = null; target.LastFrameUrl = null; target.Status = "ready"; run.Phase = "submit";
                } else { state.Clarifications = (state.Clarifications + "\nSpoken language: " + LanguageName(language) + ". " + reply.Text).TakeLastText(12000); run.Phase = "plan"; }
                break;
            case "clip-audio-review":
                var audioClip = state.Clips[state.Current];
                if (reply.Choice == "balance" && Dedicated(state)) {
                    var settings = JsonSerializer.Deserialize<MixReply>(reply.Text ?? "", new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                        ?? throw new InvalidOperationException("Choose valid audio levels.");
                    if (!double.IsFinite(settings.SpeechDb) || !double.IsFinite(settings.MusicDb) || settings.SpeechDb is < -18 or > 6 || settings.MusicDb is < -18 or > 6)
                        throw new InvalidOperationException("Audio levels must be between -18 and +6 dB.");
                    if (state.Clips.Any(c => c.ApprovedAudioVersion != null) && settings.ContinuousMusic != state.AudioPlan?.ContinuousMusic)
                        throw new InvalidOperationException("Keep the approved music continuity setting for this story.");
                    state.Mix = new(settings.SpeechDb, settings.MusicDb);
                    state.MusicContinuity = settings.ContinuousMusic;
                    run.Phase = "clip-audio-plan"; break;
                }
                if (reply.Choice == "music" && Dedicated(state)) { await AskBackground(run, state, "clip-audio-plan", ct); break; }
                if (reply.Choice == "retry") { run.Phase = "clip-audio-process"; break; }
                if (reply.Choice == "narration" && Dedicated(state)) {
                    audioClip.AudioPreview = null; audioClip.AudioPreviewReview = null;
                    state.Clarifications = (state.Clarifications + "\nNarration correction: " + answer).TakeLastText(12000);
                    state.NarrationReturnPhase = "review-wait"; run.Phase = "narration-plan"; break;
                }
                if (reply.Choice != "approve" || audioClip.AudioPreview is not { ImprovedAvailable: true, PreviewVersion: not null })
                    throw new InvalidOperationException("An available audio preview must be explicitly approved.");
                audioClip.ApprovedAudioVersion = audioClip.AudioPreview.PreviewVersion;
                audioClip.AudioDecision = Dedicated(state) ? "replace-with-narration" : "keep-original";
                if (!Dedicated(state)) audioClip.SpeechAccepted = true;
                AdvanceClip(run, state, $"Clip {state.Current + 1}/{state.Clips.Count}");
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
            case "boundary":
                if (reply.Choice is "continue" or "prompt") {
                    state.Clips[state.Current].BoundaryBypass = true;
                    // Only the prompt choice instructs the next clip. The other actions do not carry typed words.
                    state.Clips[state.Current].BoundaryNote = reply.Choice == "prompt" ? reply.Text!.Trim().Truncate(2000) : null;
                }
                else if (reply.Choice == "recheck") {
                    // Shift the samples and look again. Typed words belong to the prompt choice, not this look.
                    var previous = state.Clips[state.Current - 1];
                    var evidence = await production!.EvidenceAsync($"{run.Id}-{state.Current}-{previous.RenderAttempt}", true, ct);
                    var report = await critic!.CheckAsync(previous.VideoPrompt ?? previous.Prompt, evidence, null, ct);
                    if (evidence.Ending?.Valid() == true && report.Ending is { } ending && ending.Valid()) {
                        if (evidence.EndingMotion is { Consistent: false }) ending = ending with { Status = "uncertain" };
                        previous.Boundary = new(previous.ShotId, $"{run.Id}-{state.Current}-{previous.RenderAttempt}", DateTimeOffset.UtcNow,
                            report.Model ?? "configured-vision-model", evidence.Ending.Seconds, ending, evidence.DurationSeconds);
                        await SaveBoundaryAsync(run.WorkspaceId, previous.Boundary, ct);
                    }
                } else throw new InvalidOperationException("Choose how to continue from the saved ending.");
                run.Phase = "prepare"; break;
            case "continuity":
                if (reply.Choice == "recheck") { run.Phase = "critic"; break; }
                if (reply.Choice == "regenerate") {
                    var target = state.Clips[state.Current];
                    if (target.VisualRetries >= 2) throw new InvalidOperationException("Visual correction limit reached.");
                    target.TransitionCorrection = string.Join("; ", target.Continuity?.Concerns ?? []).Truncate(2000);
                    target.ReplacesJobKey = RenderKey(run, state); target.VisualRetries++; target.RenderAttempt++;
                    ResetRender(target); target.Continuity = null; target.Speech = null; target.SpeechVerified = null;
                    target.Accepted = false; target.VisualAccepted = false; target.SpeechAccepted = false;
                    target.AudioDecision = null; target.AudioReviewRequired = false;
                    target.VideoUrl = null; target.LastFrameUrl = null; target.Status = "ready"; run.Phase = "submit";
                    break;
                }
                if (reply.Choice != "accept") throw new InvalidOperationException("Explicit acceptance is required.");
                ApproveVisual(run, state, $"Clip {state.Current + 1}/{state.Clips.Count}"); break;
            case "timeout": state.Clips[state.Current].WaitExtendedAt = DateTimeOffset.UtcNow; break;
            case "render-failed":
                state.Clips[state.Current].RenderAttempt++; ResetRender(state.Clips[state.Current]); run.Phase = "submit"; break;
            case "retry":
                // Default retry clears the question and re-runs the phase.
                // keep-original bypasses a failing soundtrack step without looping.
                if (reply.Choice == "keep-original") {
                    if (run.Phase == "audio-process") {
                        state.AudioChoice = "original";
                        run.Phase = "finish";
                    }
                    else if (run.Phase == "clip-audio-process" && state.Current < state.Clips.Count) {
                        var clip = state.Clips[state.Current];
                        clip.KeepOriginalAudio = true;
                        clip.AudioDecision = "keep-original";
                        if (!Dedicated(state)) clip.SpeechAccepted = true;
                        AdvanceClip(run, state, $"Clip {state.Current + 1}/{state.Clips.Count}");
                    }
                }
                // continue / skip-timing / continue-untimed unstick speech clocks without looping (CLRM-48/49).
                else if (SceneSpeechTiming.IsSkipChoice(reply.Choice) && state.Current < state.Clips.Count) {
                    var clip = state.Clips[state.Current];
                    clip.Speech = SceneSpeechTiming.MarkSkipped(clip.Speech);
                    if (clip.VisualAccepted) {
                        clip.SpeechAccepted = true;
                        if (!Dedicated(state)) clip.AudioDecision ??= "keep-original";
                        ReviewChecks(run, state, $"Clip {state.Current + 1}/{state.Clips.Count}");
                    }
                    else run.Phase = "speech-check";
                }
                else if (reply.Choice == "regenerate" && state.Current < state.Clips.Count) {
                    var target = state.Clips[state.Current];
                    if (target.SpeechRetries >= 2) throw new InvalidOperationException("Speech correction limit reached.");
                    target.ReplacesJobKey = RenderKey(run, state);
                    target.SpeechRetries++; target.RenderAttempt++;
                    ResetRender(target);
                    target.ChecksVersion = Math.Max(target.ChecksVersion, 1);
                    target.Speech = null; target.Continuity = null; target.Accepted = false;
                    target.VisualAccepted = false; target.SpeechAccepted = false; target.SpeechVerified = null;
                    target.AudioDecision = null; target.AudioReviewRequired = false;
                    target.VideoUrl = null; target.LastFrameUrl = null; target.Status = "ready"; run.Phase = "submit";
                }
                break;
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
            Changes = extraction.Changes.Select(c => c with {
                Subject = Id(c.Subject), Target = Id(c.Target),
                // The reducer also accepts exact entity references in value for typed relations.
                // Rebase those references too, while preserving ordinary attribute/dialogue text.
                Value = c.Target.Length == 0 && c.Property is ("held_by" or "on" or "in" or "located_at" or
                    "has" or "holds" or "holding" or "carries" or "carrying" or "consumed_by" or "destroyed_by" or "owned_by" or "owns")
                    ? Id(c.Value) : c.Value
            }).ToArray(),
            Events = extraction.Events?.Select(e => e with { Participants = e.Participants.Select(Id).ToArray() }).ToArray()
        };
    }
    private async Task<ScenePromptExtraction> InterpretPlanAsync(SceneWorkspace workspace, string prompt, string model, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var seconds = options.CurrentValue.PlanCheckTimeoutSeconds;
        deadline.CancelAfter(TimeSpan.FromSeconds(double.IsFinite(seconds) ? Math.Clamp(seconds, .05, 420) : 360));
        var updates = System.Threading.Channels.Channel.CreateUnbounded<ScenePromptProgress>();
        string? lastStage = null;
        var reporter = new ScenePromptProgressReporter(p => {
            if (p.Stage == "continuity" && lastStage == p.Stage) return;
            lastStage = p.Stage; updates.Writer.TryWrite(p);
        });
        var recording = RecordProgressAsync();
        try { return await interpreter.InterpretAsync(workspace, prompt, model, reporter, deadline.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
            throw new ScenePromptInterpretationException("The shot check reached its workflow deadline.", model, prompt,
                [new(1, [ScenePromptDiagnostics.Issue(prompt, "preview_timeout", "$", "The shot check took too long. No shot was committed.")])]);
        }
        finally { updates.Writer.TryComplete(); await recording; }
        async Task RecordProgressAsync() {
            await foreach (var update in updates.Reader.ReadAllAsync()) await SceneVideoTrace.PlanningProgressAsync(update);
        }
    }
    private static readonly IReadOnlyList<AgentChoice> PlanningChoices = [
        new("retry", "Retry failed check · keep checked sections"), new("reset-plan", "Start this shot’s planning over"),
        new("revise-shot", "Revise this shot’s instructions"), new("cancel", "Stop here")];
    private static IReadOnlyList<AgentChoice> RecoveryChoices(AgentRun run, SceneVideoState state)
    {
        // Always offer Stop — Retry-only recovery is what left sequences waiting for hours.
        if (state.Current < state.Clips.Count && run.Phase is "submit" or "poll")
            return [new("retry", "Reconnect and retry"), new("restart", "Start this clip over · keep completed clips"), new("cancel", "Stop here")];
        var choices = new List<AgentChoice> { new("retry", "Retry this step") };
        var clip = state.Current < state.Clips.Count ? state.Clips[state.Current] : null;
        // Offer keep-original on soundtrack processing conflicts so the run can advance without looping.
        if (run.Phase is "clip-audio-process" or "audio-process")
            choices.Add(new("keep-original", "Keep original audio"));
        // Only offer word-timing skip on speech-check or when the current clip actually has untimed/unavailable speech
        // (never unconditionally on project/audio-process conflicts).
        if (run.Phase == "speech-check" ||
            (run.Phase is not ("project" or "clip-audio-process" or "audio-process") && SceneSpeechTiming.OfferSkip(clip?.Speech)))
            choices.Add(new("continue", "Continue without word timing"));
        if (clip != null && state.SpeechReviewEnabled && clip.SpeechRetries < 2 &&
            run.Phase is "speech-check" or "clip-audio-process" or "clip-audio-plan" or "clip-audio-check")
            choices.Add(new("regenerate", "Regenerate this clip"));
        choices.Add(new("cancel", "Stop here"));
        return choices;
    }

    private static void ResetRender(SceneVideoClip clip)
    {
        clip.Boundary = null;
        clip.AudioPreview = null; clip.AudioPreviewReview = null; clip.ApprovedAudioVersion = null;
        clip.KeepOriginalAudio = false;
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
    private static void Ask(AgentRun run, string kind, string prompt, IReadOnlyList<AgentChoice> choices, string? code = null)
    { run.Status = "waiting"; run.Reply = null; run.Question = new(Guid.NewGuid().ToString("N"), kind, prompt, choices, Code: code); }
}

internal static class WorkflowText
{
    public static string Truncate(this string value, int length) => value.Length <= length ? value : value[..length];
    public static string TakeLastText(this string value, int length) => value.Length <= length ? value : value[^length..];
}
