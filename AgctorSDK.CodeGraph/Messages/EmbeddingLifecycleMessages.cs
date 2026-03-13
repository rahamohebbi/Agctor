using System;

namespace AgctorSDK.CodeGraph.Messages
{
    /// <summary>
    /// High-level lifecycle states for the shared embedding store.
    /// </summary>
    public enum EmbeddingLifecycleState
    {
        NotReady = 0,
        Indexing = 1,
        Ready = 2,
        Stale = 3,
        Failed = 4
    }

    /// <summary>
    /// Ensures embeddings exist and are fresh enough for semantic search.
    /// </summary>
    public record EnsureEmbeddingsReadyMessage(bool ForceRefresh = false, string? Reason = null);

    /// <summary>
    /// Marks the embedding store stale after code changes.
    /// </summary>
    public record MarkEmbeddingsStaleMessage(string? Reason = null);

    /// <summary>
    /// Requests the current lifecycle status for the embedding store.
    /// </summary>
    public record GetEmbeddingStatusMessage();

    /// <summary>
    /// Result returned after ensuring the embedding store is ready.
    /// </summary>
    public record EmbeddingReadyResult(
        bool IsReady,
        bool TriggeredIndexing,
        EmbeddingLifecycleState State,
        int GraphVersion,
        int IndexedGraphVersion,
        DateTimeOffset? LastIndexedAt,
        string? LastError);

    /// <summary>
    /// Snapshot of embedding lifecycle status for UI and diagnostics.
    /// </summary>
    public record EmbeddingStatusResult(
        EmbeddingLifecycleState State,
        int GraphVersion,
        int IndexedGraphVersion,
        DateTimeOffset? LastIndexedAt,
        string? LastError)
    {
        public bool IsReady => State == EmbeddingLifecycleState.Ready && GraphVersion == IndexedGraphVersion;
    }
}
