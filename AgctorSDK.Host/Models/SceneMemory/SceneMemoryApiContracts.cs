using System.ComponentModel.DataAnnotations;
using AgctorSDK.Core.SceneMemory;

namespace AgctorSDK.Host.Models.SceneMemory;

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
    [StringLength(200)] string? Model = null);

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
    IReadOnlyList<SceneTimelineDelta> Changes, IReadOnlyList<SceneFact> Events, IReadOnlyList<SceneFact> StateAfter);

public sealed record SceneTimelineDelta(string EntityId, string Property, SceneFact? Before, SceneFact? After);
