using AgctorSDK.Core.SceneMemory;

namespace AgctorSDK.Host.Services.VideoWorkflow;

public sealed record StartSceneVideoRun(string RequestId, string? WorkspaceId, string Prompt,
    int TotalSeconds = 30, int ClipSeconds = 15, string? Model = null, bool ReviewPlan = false, string? StoryMode = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? AudioModel = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? SpeechLanguage = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? ProjectId = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? NarrationMode = null,
    /// <summary>1-based film slot for continue. Cynelo trims later clips (or replaces that slot) so the new take lands here instead of appending past it.</summary>
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] int? ReplaceClipIndex = null,
    /// <summary>Previous-clip media for continuity: <c>final</c> prefers New mix / improved audio-video; <c>original</c> uses the raw take. Continue defaults to final when omitted.</summary>
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? ContinuityMedia = null);
public sealed class SceneVideoState
{
    public StartSceneVideoRun Input { get; set; } = null!;
    public string Model { get; set; } = "";
    public int MaxAttempts { get; set; } = 3;
    public string Instructions { get; set; } = "";
    public string WorkspaceRevision { get; set; } = "";
    public string Clarifications { get; set; } = "";
    public string? StoryId { get; set; }
    public string? StoryName { get; set; }
    public List<string> HumanNotes { get; set; } = [];
    public List<SceneVideoClip> Clips { get; set; } = [];
    public int Current { get; set; }
    public bool PlanReviewed { get; set; }
    public bool ProductionEnabled { get; set; }
    public string? ProjectId { get; set; }
    public string? FinalVideoUrl { get; set; }
    public SceneWorkspace? ReviewWorkspace { get; set; }
    public int ReviewIndex { get; set; }
    public ScenePlanProblem? PlanProblem { get; set; }
    public IReadOnlyList<SceneEntity> PlannedEntities { get; set; } = [];
    public bool AudioEnabled { get; set; }
    public bool? PreviousContinuousMusic { get; set; }
    public SceneAudioPlan? AudioPlan { get; set; }
    public SceneAudioDraft? AudioDraft { get; set; }
    public SceneContinuityReport? AudioReview { get; set; }
    public string? AudioChoice { get; set; }
    public bool SpeechReviewEnabled { get; set; }
    public string? NarrationReturnPhase { get; set; }
    public bool RestoreOriginalNarration { get; set; }
    public string BackgroundMusic { get; set; } = "preserve";
    public string MusicProfile { get; set; } = "close";
    public SceneAudioMix? Mix { get; set; }
    public bool? MusicContinuity { get; set; }
    public string? BackgroundReturnPhase { get; set; }
    public string NarrationModel { get; set; } = "kokoro";
    /// <summary>ElevenLabs stability. Kokoro stores it and does not apply it.</summary>
    public double? NarrationSoftness { get; set; }
    /// <summary>Configured cloud voice id or a label. Kokoro stays af_heart.</summary>
    public string? NarrationAccent { get; set; }
    /// <summary>When set, replaces the measured pace for narration. Empty means match the original window.</summary>
    public double? NarrationPaceOverride { get; set; }
    public string ExpectedSpeechLanguage { get; set; } = "auto";
}
public sealed class SceneVideoClip
{
    public string ShotId { get; set; } = "";
    public string Prompt { get; set; } = "";
    public int DurationSeconds { get; set; }
    public string Status { get; set; } = "planned";
    public ScenePromptPreview? Preview { get; set; }
    public bool BeforePromptsWereNull { get; set; }
    public string? VideoPrompt { get; set; }
    public string? VideoUrl { get; set; }
    public string? LastFrameUrl { get; set; }
    public int RenderAttempt { get; set; } = 1;
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? WaitExtendedAt { get; set; }
    public DateTimeOffset? LastCheckedAt { get; set; }
    public bool? LastCheckSucceeded { get; set; }
    public string? RendererStatus { get; set; }
    public string? ProviderJobId { get; set; }
    public SceneRenderReferences? References { get; set; }
    public SceneRenderProgress? Progress { get; set; }
    public ScenePlanDetails? PlanDetails { get; set; }
    public SceneContinuityReport? Continuity { get; set; }
    public SceneBoundaryObservation? Boundary { get; set; }
    public bool BoundaryBypass { get; set; }
    public string? TransitionCorrection { get; set; }
    /// <summary>Creator words attached to “continue unverified”. They do not mark the ending observed.</summary>
    public string? BoundaryNote { get; set; }
    public int VisualRetries { get; set; }
    public bool Accepted { get; set; }
    public SceneSpeechReport? Speech { get; set; }
    public string? SpeechInstruction { get; set; }
    public string? ReplacesJobKey { get; set; }
    public int SpeechRetries { get; set; }
    public int ChecksVersion { get; set; }
    public bool VisualAccepted { get; set; }
    public bool SpeechAccepted { get; set; }
    public bool? SpeechVerified { get; set; }
    public string? ExpectedSpeechText { get; set; }
    public bool AudioReviewRequired { get; set; }
    // Explicit human disposition; a planned replacement is not evidence of verified speech.
    public string? AudioDecision { get; set; }
    public SceneAudioDraft? AudioPreview { get; set; }
    public SceneContinuityReport? AudioPreviewReview { get; set; }
    public string? ApprovedAudioVersion { get; set; }
    public string? NarrationText { get; set; }
    // Version zero preserves the request body of already-submitted legacy takes.
    public int NativeSpeechVersion { get; set; }
}
public sealed record SceneVideoPlan(IReadOnlyList<string> Clips, string? Question = null, IReadOnlyList<string>? Choices = null);
public interface ISceneVideoPlanner
{
    Task<SceneVideoPlan> PlanAsync(SceneWorkspace workspace, SceneVideoState state, IReadOnlyList<int> durations, CancellationToken ct);
}

/// <summary>A provider bridge must make EnsureAsync idempotent for the supplied key and identical inputs.</summary>
public interface ISceneVideoRenderer
{
    bool IsConfigured { get; }
    Task<SceneRenderJob> EnsureAsync(string key, SceneRenderRequest request, CancellationToken ct);
    Task<SceneRenderJob> GetAsync(string key, CancellationToken ct);
    Task<SceneRenderJob> ResetAsync(string key, CancellationToken ct) =>
        throw new SceneRenderInterventionException("The renderer needs an update before this clip can be restarted.", "reset_unsupported");
}
public sealed record SceneRenderRequest(string RunId, string WorkspaceId, string ShotId, int ClipNumber,
    int ClipCount, int DurationSeconds, string Prompt, string? FirstFrameUrl,
    string? StoryId = null, string? ExpectedRevision = null, string? PreviousJobKey = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? StoryMode = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? ProjectId = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? ReplacesJobKey = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? SpeechInstruction = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? BoundaryInstruction = null,
    /// <summary>When set, Cynelo chains from final mix / improved media for the previous film clip.</summary>
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? ContinuityMedia = null,
    /// <summary>1-based film slot this take must occupy after trim/replace (usually equals ReplaceClipIndex for the first clip of a continue).</summary>
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] int? ReplaceClipIndex = null);
public sealed record SceneRenderJob(string Id, string Status, string? VideoUrl = null, string? LastFrameUrl = null,
    string? Error = null, SceneRenderReferences? References = null, SceneRenderProgress? Progress = null);
public sealed record SceneRenderReferences(int Selected, int Available, int Designs);
public sealed record SceneRenderProgress(int? Percent, string Stage, string? Detail, DateTimeOffset? UpdatedAt, bool Estimated = true);
public sealed class SceneVideoOptions
{
    public string? StorePath { get; set; }
    public bool WorkerEnabled { get; set; } = true;
    /// <summary>Trusted server configuration, never a URL accepted from a run prompt.</summary>
    public string? RendererBaseUrl { get; set; } = "http://127.0.0.1:5080/api/agctor/render";
    public int PollSeconds { get; set; } = 5;
    public int WaitingPollSeconds { get; set; } = 30;
    public int RenderTimeoutMinutes { get; set; } = 45;
    /// <summary>Must finish and checkpoint before the specialist's eight-minute mailbox timeout.</summary>
    public double PlanCheckTimeoutSeconds { get; set; } = 360;
}

/// <summary>Idempotent story provisioning in the configured rendering studio; no media generation.</summary>
public interface ISceneVideoStoryGateway
{
    Task<SceneVideoStory> EnsureAsync(string key, string name, CancellationToken ct);
    Task<IReadOnlyList<SceneVideoStory>> ListAsync(CancellationToken ct);
}
public sealed record SceneVideoStory(string WorkspaceId, string StoryId, string Name);

public sealed record ScenePlanDetails(IReadOnlyList<ScenePlanEntity> Participants, IReadOnlyList<ScenePlanReference> References);
public sealed record ScenePlanEntity(string Name, string Kind);
public sealed record ScenePlanReference(string Name, string DesignId, IReadOnlyList<string> ImageIds, string? PortraitId);
public sealed record SceneContinuityReport(string Status, string Summary, IReadOnlyList<string> Concerns,
    SceneBoundaryState? Ending = null, string? TransitionStatus = null, string? Model = null);
public sealed record SceneBoundaryFrames(IReadOnlyList<string> Frames, IReadOnlyList<double> Seconds)
{
    public bool Valid() => Frames is { Count: 3 } && Seconds is { Count: 3 } &&
        Frames.All(f => !string.IsNullOrWhiteSpace(f) && f.Length <= 8_000_000) &&
        Seconds.All(t => double.IsFinite(t) && t >= 0) && Seconds[0] < Seconds[1] && Seconds[1] < Seconds[2];
}
public sealed record SceneVideoEvidence(IReadOnlyList<string> Frames, string? PreviousFrame = null, bool PreviousExpected = false,
    SceneBoundaryFrames? Opening = null, SceneBoundaryFrames? Ending = null, SceneBoundaryFrames? PreviousEnding = null, double? DurationSeconds = null,
    SceneMotionSummary? EndingMotion = null);
/// <summary>Frame-difference of the three ending stills. Not optical flow.</summary>
public sealed record SceneMotionSummary(double Change, string Direction, bool Consistent);
public sealed record SceneVideoProject(string ProjectId, bool? ContinuousMusic = null);
public sealed record SceneVideoDelivery(string VideoUrl);
/// <summary>Idempotent studio operations, invoked only by leased specialist actors.</summary>
public interface ISceneVideoProduction
{
    Task<SceneVideoProject> EnsureProjectAsync(string runId, string storyId, string mode, string name, CancellationToken ct,
        string? projectId = null, int? replaceClipIndex = null, string? continuityMedia = null);
    Task<SceneVideoEvidence> EvidenceAsync(string key, CancellationToken ct);
    /// <summary>Recheck shifts the ending samples inside the same window. The default repeats the unshifted read.</summary>
    Task<SceneVideoEvidence> EvidenceAsync(string key, bool recheck, CancellationToken ct) => EvidenceAsync(key, ct);
    Task<SceneVideoDelivery> FinishAsync(string runId, string projectId, IReadOnlyList<string> keys, CancellationToken ct);
    Task<SceneAudioDraft> PrepareAudioAsync(string runId, string projectId, IReadOnlyList<string> keys, SceneAudioPlan plan, CancellationToken ct) => throw new NotSupportedException("Audio production is not configured.");
    Task<SceneAudioDraft> PrepareClipAudioAsync(string runId, string projectId, IReadOnlyList<string> keys, SceneAudioPlan plan, CancellationToken ct) => throw new NotSupportedException("Clip audio production is not configured.");
    Task<SceneVideoDelivery> FinishAudioAsync(string runId, string projectId, IReadOnlyList<string> keys, string choice, CancellationToken ct) => throw new NotSupportedException("Audio production is not configured.");
    Task<bool> MatchingMusicAvailableAsync(CancellationToken ct) => Task.FromResult(false);
    Task<IReadOnlyList<string>> NarrationModelsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(["kokoro"]);
    Task<SceneSpeechReport> InspectSpeechAsync(string key, CancellationToken ct) => throw new NotSupportedException("Speech inspection is not configured.");
}
public sealed record SceneAudioPlan(bool ContinuousMusic, string Summary, string? Separator = null, SceneNarrationPlan? Narration = null, IReadOnlyList<SceneAudioApproval>? Approved = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? BackgroundMusic = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] SceneAudioMix? Mix = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? MusicProfile = null);
public sealed record SceneAudioMix(double SpeechDb = 0, double MusicDb = 0);
public sealed record SceneAudioApproval(int ClipNumber, string Version);
public sealed record SceneNarrationPlan(string Model, string Voice, string Language, IReadOnlyList<string> Scripts,
    IReadOnlyList<SceneNarrationWindow>? Windows = null, double? Softness = null, string? Accent = null);
/// <summary>Where and how long this clip’s narration should sit. Speed is applied by the narrator from the target, unless PaceOverride is set.</summary>
public sealed record SceneNarrationWindow(double? TargetSeconds = null, double StartSeconds = 0.2, double? PaceOverride = null);
public sealed record SceneSpeechWord(string Text, double StartSeconds, double EndSeconds);
public sealed record SceneSpeechReport(string Status, string? Language, double Confidence, string Transcript, string Summary,
    IReadOnlyList<SceneSpeechIssue>? Issues = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SceneSpeechWord>? Words = null);
// UTF-16 offsets into the original transcript. Zero length denotes missing expected words.
public sealed record SceneSpeechIssue(int Start, int Length, string Reason);
public sealed record SceneSpeechUnderstanding(bool Understood, IReadOnlyList<SceneSpeechIssue> Issues, bool Available = true);
public sealed record SceneAudioDraft(bool ImprovedAvailable, string Summary, System.Text.Json.JsonElement Evidence, string? PreviewVersion = null);
public interface ISceneAudioEngineer
{
    Task<IReadOnlyList<string>> NarrationAsync(string prompt, string model, IReadOnlyList<SceneVideoClip> clips, CancellationToken ct) => throw new NotSupportedException("Narration planning is not configured.");
    Task<SceneAudioPlan> PlanAsync(string prompt, string model, CancellationToken ct);
    Task<SceneContinuityReport> ReviewAsync(string prompt, string model, SceneAudioDraft draft, CancellationToken ct);
    Task<bool> SpeechUnderstoodAsync(string prompt, string model, SceneSpeechReport speech, CancellationToken ct) => Task.FromResult(false);
    async Task<SceneSpeechUnderstanding> ReviewSpeechAsync(string prompt, string model, SceneSpeechReport speech, CancellationToken ct) =>
        new(await SpeechUnderstoodAsync(prompt, model, speech, ct), []);
    Task<string> SpeechLanguageAsync(string prompt, string model, CancellationToken ct) => Task.FromResult("unknown");
}
public interface ISceneVideoCritic
{
    Task<SceneContinuityReport> CheckAsync(string prompt, SceneVideoEvidence current, SceneVideoEvidence? previous, CancellationToken ct);
}
