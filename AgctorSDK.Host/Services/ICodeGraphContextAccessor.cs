using AgctorSDK.CodeGraph.Persistence;
using AgctorSDK.Host.Models;

namespace AgctorSDK.Host.Services;

/// <summary>
/// Provides access to the current CodeGraph context when code-graph-demo scenario has been set up (PRD-006).
/// </summary>
public interface ICodeGraphContextAccessor
{
    /// <summary>
    /// Gets the current CodeGraph context (actor tree + embedding summary), or null if no CodeGraph scenario is active.
    /// </summary>
    CodeGraphContextDto? GetCurrent();

    /// <summary>
    /// Gets the current context asynchronously, with live embedding count if a provider is registered.
    /// </summary>
    Task<CodeGraphContextDto?> GetCurrentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the current context. Called by CodeGraphDemoScenario after setup.
    /// </summary>
    void SetCurrent(CodeGraphContextDto? context);

    /// <summary>
    /// Registers a provider that returns the current actor tree (e.g. live-serialized solution).
    /// When set, GetCurrent/GetCurrentAsync use it so the tree reflects index and code changes.
    /// </summary>
    void SetActorTreeProvider(Func<ActorSerializer.ActorDto?>? provider);

    /// <summary>
    /// Registers a provider that returns the current embedding store vector count (e.g. from the scenario's vector store).
    /// When set, GetCurrentAsync uses this for EmbeddingStoreSummary.VectorCount.
    /// </summary>
    void SetEmbeddingCountProvider(Func<CancellationToken, Task<int>>? provider);

    /// <summary>
    /// Registers a provider that returns all embedding records for debugging/visualization.
    /// </summary>
    void SetEmbeddingRecordsProvider(Func<CancellationToken, Task<IReadOnlyList<EmbeddingRecordDto>>>? provider);

    /// <summary>
    /// Gets all embedding records for debugging/visualization (when a provider is registered).
    /// </summary>
    Task<IReadOnlyList<EmbeddingRecordDto>> GetEmbeddingRecordsAsync(CancellationToken cancellationToken = default);
}
