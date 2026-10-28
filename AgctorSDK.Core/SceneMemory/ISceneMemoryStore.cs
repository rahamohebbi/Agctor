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
    /// <summary>Loads a workspace or throws KeyNotFoundException.</summary>
    Task<SceneWorkspace> GetAsync(string workspaceId, CancellationToken cancellationToken = default);
    /// <summary>Adds a named entity; names are unique within a workspace.</summary>
    Task<SceneWorkspace> AddEntityAsync(string workspaceId, string name, string kind, CancellationToken cancellationToken = default);
    /// <summary>Appends an explicit property change. Never infers state from its note.</summary>
    Task<SceneWorkspace> RecordAsync(string workspaceId, string entityId, string property, string value, string? note, CancellationToken cancellationToken = default);
    /// <summary>Retrieves current or historical facts by entity ID or exact name mentioned in a question.</summary>
    Task<SceneQueryResult> QueryAsync(string workspaceId, SceneQuery query, CancellationToken cancellationToken = default);
}

/// <summary>A scene workspace and its append-only fact history.</summary>
public sealed record SceneWorkspace(string Id, string Name, DateTimeOffset CreatedAt,
    IReadOnlyList<SceneEntity> Entities, IReadOnlyList<SceneFact> History);
/// <summary>A stable identity with a human-maintained type.</summary>
public sealed record SceneEntity(string Id, string Name, string Kind);
/// <summary>An explicit state change; sequence is entry order, not inferred story time.</summary>
public sealed record SceneFact(long Sequence, string EntityId, string Property, string Value, string Note, DateTimeOffset RecordedAt);
/// <summary>Supply EntityId or mention an entity's exact name in Question. AtSequence is optional.</summary>
public sealed record SceneQuery(string? EntityId = null, string? Question = null, long? AtSequence = null);
/// <summary>Grounded retrieval result, including the source changes for each returned property.</summary>
public sealed record SceneQueryResult(string WorkspaceId, long AtSequence, string Status, string Answer,
    IReadOnlyList<SceneEntity> Matches, IReadOnlyList<SceneFact> Facts);

/// <summary>Shared tool identity, so agents need not reference the tool implementation assembly.</summary>
public static class SceneMemoryTool
{
    /// <summary>Host and agent tool actor type name.</summary>
    public const string TypeName = "SceneMemoryQueryTool";
}
