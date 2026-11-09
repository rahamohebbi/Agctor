using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.SceneMemory;

/// <summary>Manually maintained scene facts. Independent of actors, agents, models and RAG providers.</summary>
public interface ISceneMemoryStore
{
    /// <summary>Lists durable scene workspaces.</summary>
    Task<IReadOnlyList<SceneWorkspace>> ListAsync(CancellationToken cancellationToken = default);
    /// <summary>Creates an empty workspace.</summary>
    Task<SceneWorkspace> CreateAsync(string name, CancellationToken cancellationToken = default);
    /// <summary>Creates or resolves a workspace by the caller's stable application/project/story identity.</summary>
    Task<SceneWorkspace> ConnectAsync(string name, SceneWorkspaceConnection connection, string? workspaceId = null, CancellationToken cancellationToken = default);
    /// <summary>Upserts a Studio instance or explicitly links an existing discovered entity. Does not rewrite story facts.</summary>
    Task<SceneWorkspace> SyncStudioAsync(string workspaceId, string instanceKey, SceneStudioInput input, CancellationToken cancellationToken = default);
    /// <summary>Atomically replace or unlink an entity's Studio binding, preserving all scene facts and identity.</summary>
    Task<SceneWorkspace> ChangeStudioLinkAsync(string workspaceId, string entityId, string expectedRevision,
        string? instanceKey, SceneStudioInput? input, CancellationToken cancellationToken = default);
    /// <summary>Loads a workspace or throws KeyNotFoundException.</summary>
    Task<SceneWorkspace> GetAsync(string workspaceId, CancellationToken cancellationToken = default);
    /// <summary>Adds a named entity; names are unique within a workspace.</summary>
    Task<SceneWorkspace> AddEntityAsync(string workspaceId, string name, string kind, CancellationToken cancellationToken = default);
    /// <summary>Appends an explicit property change. Never infers state from its note.</summary>
    Task<SceneWorkspace> RecordAsync(string workspaceId, string entityId, string property, string value, string? note, CancellationToken cancellationToken = default);
    /// <summary>Sets an object's holder in one change; replaces its previous holder and preserves history.</summary>
    Task<SceneWorkspace> SetHolderAsync(string workspaceId, string objectId, string holderId, string? note, CancellationToken cancellationToken = default);
    /// <summary>Atomically applies a validated prompt preview against its original workspace revision.</summary>
    Task<SceneWorkspace> ApplyPromptAsync(string workspaceId, ScenePromptPreview preview, CancellationToken cancellationToken = default);
    /// <summary>Updates presentation metadata for an applied prompt without changing state or sequence order.</summary>
    Task<SceneWorkspace> UpdatePromptMetadataAsync(string workspaceId, string promptId, string? sceneLabel,
        string? shotLabel, double? durationSeconds, CancellationToken cancellationToken = default);
    /// <summary>Explicitly replaces only one shot's design snapshots after a revision-checked review.</summary>
    Task<SceneWorkspace> PinShotDesignsAsync(string workspaceId, string shotId, string expectedRevision, CancellationToken cancellationToken = default);
    /// <summary>Deletes one applied shot and its facts atomically, preserving entities and other shots.</summary>
    Task<SceneWorkspace> DeleteShotAsync(string workspaceId, string shotId, string expectedRevision, CancellationToken cancellationToken = default);
    /// <summary>Retrieves current or historical facts by entity ID or exact name mentioned in a question.</summary>
    Task<SceneQueryResult> QueryAsync(string workspaceId, SceneQuery query, CancellationToken cancellationToken = default);
}

/// <summary>A scene workspace and its append-only fact history.</summary>
public sealed record SceneWorkspace(string Id, string Name, DateTimeOffset CreatedAt,
    IReadOnlyList<SceneEntity> Entities, IReadOnlyList<SceneFact> History,
    IReadOnlyList<ScenePromptRecord>? Prompts = null, SceneWorkspaceConnection? Connection = null, IReadOnlyList<string>? DeletedShotIds = null);
/// <summary>A stable identity with a human-maintained type.</summary>
public sealed record SceneEntity(string Id, string Name, string Kind, SceneStudioBinding? Studio = null, long? CreatedAtSequence = null);
/// <summary>An explicit state change; sequence is entry order, not inferred story time.</summary>
public sealed record SceneFact(long Sequence, string EntityId, string Property, string Value, string Note, DateTimeOffset RecordedAt,
    string? RelatedEntityId = null, bool Removed = false, string? PromptId = null,
    bool IsEvent = false, IReadOnlyList<string>? ParticipantIds = null, string? EventMode = null);
/// <summary>Supply EntityId or mention an entity's exact name in Question. AtSequence is optional.</summary>
public sealed record SceneQuery(string? EntityId = null, string? Question = null, long? AtSequence = null);
/// <summary>Grounded retrieval result, including the source changes for each returned property.</summary>
public sealed record SceneQueryResult(string WorkspaceId, long AtSequence, string Status, string Answer,
    IReadOnlyList<SceneEntity> Matches, IReadOnlyList<SceneFact> Facts, IReadOnlyList<SceneFact>? Events = null);

/// <summary>Shared tool identity, so agents need not reference the tool implementation assembly.</summary>
public static class SceneMemoryTool
{
    /// <summary>Host and agent tool actor type name.</summary>
    public const string TypeName = "SceneMemoryQueryTool";
}
