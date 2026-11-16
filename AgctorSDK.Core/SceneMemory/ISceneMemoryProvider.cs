using AgctorSDK.Core.Retrieval;

namespace AgctorSDK.Core.SceneMemory;

/// <summary>Read-only scene retrieval. Entity updates remain on ISceneMemoryStore.</summary>
public interface ISceneMemoryProvider : IRetrievalProvider<SceneMemoryQueryRequest, SceneQueryResult>
{
    System.Threading.Tasks.Task<SceneTimedEventResult> SearchEventsAsync(string workspaceId, SceneTimedEventQuery query,
        System.Threading.CancellationToken cancellationToken = default);
}

/// <summary>
/// Provider-level query, analogous to RagQueryRequest. WorkspaceId is required; supply Query (an exact
/// entity name in a question) or EntityId. AtSequence selects historical state; omission selects latest.
/// </summary>
public sealed record SceneMemoryQueryRequest(string WorkspaceId, string? Query = null,
    string? EntityId = null, long? AtSequence = null);
