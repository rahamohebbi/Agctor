namespace AgctorSDK.Core.ProjectMemory.Scenarios;

public interface IScenarioFlowRuntimeStore
{
    Task<ScenarioFlowRuntimeSnapshot?> LoadAsync(
        string projectRoot,
        string sessionId,
        string scenarioId,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        string projectRoot,
        string sessionId,
        string scenarioId,
        ScenarioFlowRuntimeSnapshot snapshot,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string projectRoot,
        string sessionId,
        string scenarioId,
        CancellationToken cancellationToken = default);
}
