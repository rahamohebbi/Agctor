using AgctorSDK.Host.Models;

namespace AgctorSDK.Host.Services.Scenarios;

/// <summary>PRD-014: runs a scenario's <c>flow</c> GraphDocument (sequential mode, real LlmNode).</summary>
public interface IScenarioFlowExecutionService
{
    /// <summary>Executes flow until <c>Output</c>; uses project memory root for persona LLM calls.</summary>
    Task<ScenarioFlowRunResponse> RunAsync(string scenarioId, ScenarioFlowRunRequest request, CancellationToken cancellationToken = default);
}
