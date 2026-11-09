using System.ComponentModel.DataAnnotations;
using AgctorSDK.Core.SceneMemory;

namespace AgctorSDK.Host.Models.SceneMemory;

public sealed record RewriteSceneVideoRequest([Required, StringLength(128)] string ExpectedRevision,
    [Required, StringLength(32000)] string Prompt, [Required, StringLength(2000)] string Instruction,
    [StringLength(200)] string? Model = null);

// Public JSON contracts for the Scene Memory v1 surface. Consumers need HTTP/JSON only.
public sealed record ConnectStoryRequest(
    [Required, StringLength(100)] string Name,
    [Required, StringLength(100)] string Application,
    [Required, StringLength(200)] string ProjectId,
    [Required, StringLength(200)] string StoryId,
    [StringLength(100)] string? WorkspaceId = null);

public sealed record SyncStudioRequest(
    [Required, StringLength(200)] string AssetId,
    [Range(1, long.MaxValue)] long Version,
    [Required, StringLength(100)] string Name,
    [Required] string Kind,
    Dictionary<string, string>? Defaults = null,
    [StringLength(100)] string? EntityId = null);

public sealed record InterpretSceneRequest(
    [Required, StringLength(12000)] string Prompt,
    [StringLength(100)] string? ShotName = null,
    [StringLength(100)] string? SceneName = null,
    [Range(0.1, 3600)] double? DurationSeconds = null,
    [StringLength(200)] string? Model = null,
    [StringLength(128)] string? ExpectedRevision = null);

public sealed record EditScenePreviewRequest([Required] ScenePromptExtraction Extraction);

public sealed record SceneMemoryWorkspaceResponse(string Id, string Name, string Revision,
    SceneWorkspaceConnection? Connection, IReadOnlyList<SceneEntity> Entities, long LatestSequence);

public sealed record SceneMemoryPreviewResponse(ScenePromptPreview Preview, SceneMemoryWorkspaceResponse Proposed,
    IReadOnlyList<SceneFact> ProposedChanges);

public sealed record SceneMemoryCapabilitiesResponse(string ApiVersion, string[] StudioKinds,
    bool PersistentJobs, int PreviewLifetimeMinutes, string[] Features);

public sealed record SceneTimelineResponse(string WorkspaceId, string Revision,
    IReadOnlyList<SceneEntity> Entities, IReadOnlyList<SceneTimelineShot> Shots);

public sealed record SceneTimelineShot(string Id, int Order, string SceneName, string ShotName, string Prompt,
    double? DurationSeconds, long FirstSequence, long LastSequence, IReadOnlyList<string> IntroducedEntityIds,
    IReadOnlyList<SceneTimelineDelta> Changes, IReadOnlyList<SceneFact> Events, IReadOnlyList<SceneFact> StateAfter, SceneShotDesignReview Designs,
    IReadOnlyList<SceneEntity>? Participants = null);

public sealed record SceneTimelineDelta(string EntityId, string Property, SceneFact? Before, SceneFact? After);

public sealed record SceneContinuityResponse(string WorkspaceId, string Revision, IReadOnlyList<SceneEntity> Entities,
    IReadOnlyList<SceneFact> CurrentFacts, string? PreviousShotId, string? PreviousShotName);
public sealed record PrepareSceneVideoRequest([Required, StringLength(128)] string ExpectedRevision);
public sealed record SceneVideoPromptResponse(string WorkspaceId, string Revision, string ShotId, string ShotName,
    double? DurationSeconds, string Prompt, IReadOnlyList<SceneEntity> Entities, IReadOnlyList<SceneFact> StateBefore,
    IReadOnlyList<SceneFact> StateAfter, IReadOnlyList<string> Notices);

public sealed record ChangeSceneDesignRequest([Required, StringLength(128)] string ExpectedRevision,
    [StringLength(200)] string? InstanceKey = null, SyncStudioRequest? Design = null);

/// <summary>Explicit simulation inputs; AGCTOR cannot read Cynelo's active film or selected images.</summary>
public sealed record SimulateSceneVideoRequest(
    [Required, StringLength(128)] string ExpectedRevision,
    [Range(0, 10000)] int ExistingClips = 0,
    [Range(1, 15)] int? DurationSeconds = null,
    string[]? ReferenceLabels = null);

public sealed record SceneVideoPromptSimulationResponse(SceneVideoPromptResponse Handoff,
    string Formatter, int ExistingClips, int ClipSlot, int TargetSeconds, int ModelSeconds,
    string Workflow, string[] ReferenceLabels, string ContinuationPrompt, string ModelPrompt, string[] Warnings);
