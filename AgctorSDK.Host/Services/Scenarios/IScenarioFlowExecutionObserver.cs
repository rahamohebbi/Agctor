namespace AgctorSDK.Host.Services.Scenarios;

/// <summary>
/// Optional hooks while <see cref="ScenarioFlowGraphInterpreter"/> runs (playground SSE, tracing).
/// Router completion is signaled after routing resolves (LLM or deterministic), not when the Router node store is filled.
/// </summary>
public interface IScenarioFlowExecutionObserver
{
    Task OnNodeStartingAsync(string nodeId, string nodeType, CancellationToken cancellationToken = default);

    /// <param name="detail">Short summary (e.g. char counts, branch id, persona id).</param>
    Task OnNodeCompletedAsync(string nodeId, string nodeType, string? detail, CancellationToken cancellationToken = default);

    /// <summary>After Router resolves targets; before PersonaCall branches run. <paramref name="mergeNodeIdForParallel"/> set when multiple entries fan in.</summary>
    Task OnRouterBranchResolvedAsync(
        string routerNodeId,
        IReadOnlyList<string> orderedEntryNodeIds,
        string? mergeNodeIdForParallel,
        CancellationToken cancellationToken = default);
}
