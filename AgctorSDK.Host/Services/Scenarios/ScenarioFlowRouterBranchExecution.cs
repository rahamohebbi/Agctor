namespace AgctorSDK.Host.Services.Scenarios;

/// <summary>
/// When <see cref="ScenarioFlowRouterTargetPolicy.AllMatching"/> selects multiple branches,
/// controls whether branch subgraphs run concurrently or one after another.
/// </summary>
public enum ScenarioFlowRouterBranchExecution
{
    /// <summary>All selected branches start together (default).</summary>
    Parallel,

    /// <summary>Run each branch fully before starting the next (write paths before read paths).</summary>
    Sequential,

    /// <summary>Router LLM returns <c>branchExecutionMode</c>; heuristic fallback when omitted.</summary>
    Auto
}
