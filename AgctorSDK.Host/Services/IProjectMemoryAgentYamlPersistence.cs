using AgctorSDK.Host.Models;

namespace AgctorSDK.Host.Services;

/// <summary>
/// Persists project-memory <c>AgentDefinitionSpec</c> YAML under the configured project root.
/// Shared by <c>/api/project-memory/agents</c> and unified <c>/api/agents/definitions/…</c> (PRD-013 Phase 2).
/// </summary>
public interface IProjectMemoryAgentYamlPersistence
{
    /// <summary>Loads one agent for edit/preview; 400 when root missing, 404 when unknown id.</summary>
    Task<PersistenceResult<AgentDetailDto>> GetAgentDetailAsync(string id, CancellationToken cancellationToken);

    /// <summary>Writes YAML; <paramref name="createOnly"/> rejects when the id already exists in the loaded project.</summary>
    Task<PersistenceResult<object>> SaveAgentAsync(string id, SaveAgentRequestDto body, bool createOnly, CancellationToken cancellationToken);

    /// <summary>Deletes the on-disk spec file when it exists.</summary>
    Task<PersistenceResult<object>> DeleteAgentAsync(string id, CancellationToken cancellationToken);
}

/// <summary>Small transport for persistence outcomes without referencing MVC types.</summary>
public sealed class PersistenceResult<T>
{
    public int StatusCode { get; init; }
    public T? Data { get; init; }
    public object? Error { get; init; }
}
