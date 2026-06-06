using AgctorSDK.Core.ProjectMemory.Scenarios;
using AgctorSDK.Core.ProjectMemory.Scenarios.Messages;
using Microsoft.Extensions.Logging;

namespace AgctorSDK.Host.Services.Scenarios;

/// <summary>
/// PRD-024 Phase C: publishes domain events to a waiting <see cref="ScenarioFlowRuntimeActor"/>.
/// No-op when no snapshot is suspended on the matching <c>AwaitEvent</c>.
/// </summary>
public interface IScenarioFlowDomainEventPublisher
{
    Task<ScenarioFlowRuntimeResult?> TryResumeAsync(
        string projectRoot,
        string sessionId,
        string scenarioId,
        string eventType,
        IReadOnlyDictionary<string, object?> payload,
        CancellationToken cancellationToken = default);
}

public sealed class ScenarioFlowDomainEventPublisher : IScenarioFlowDomainEventPublisher
{
    private readonly IScenarioFlowRuntimeStore _store;
    private readonly IScenarioCatalog _catalog;
    private readonly IScenarioFlowRuntimeOrchestrator _orchestrator;
    private readonly ILogger<ScenarioFlowDomainEventPublisher> _logger;

    public ScenarioFlowDomainEventPublisher(
        IScenarioFlowRuntimeStore store,
        IScenarioCatalog catalog,
        IScenarioFlowRuntimeOrchestrator orchestrator,
        ILogger<ScenarioFlowDomainEventPublisher> logger)
    {
        _store = store;
        _catalog = catalog;
        _orchestrator = orchestrator;
        _logger = logger;
    }

    public async Task<ScenarioFlowRuntimeResult?> TryResumeAsync(
        string projectRoot,
        string sessionId,
        string scenarioId,
        string eventType,
        IReadOnlyDictionary<string, object?> payload,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectRoot)
            || string.IsNullOrWhiteSpace(sessionId)
            || string.IsNullOrWhiteSpace(scenarioId)
            || string.IsNullOrWhiteSpace(eventType))
        {
            return null;
        }

        var snapshot = await _store
            .LoadAsync(projectRoot, sessionId, scenarioId.Trim(), cancellationToken)
            .ConfigureAwait(false);
        if (snapshot == null || snapshot.Status != ScenarioFlowRuntimeStatus.WaitingForDomainEvent)
            return null;

        var expected = snapshot.AwaitingEvent?.EventType?.Trim();
        if (!string.IsNullOrEmpty(expected)
            && !string.Equals(expected, eventType.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug(
                "Scenario flow domain event {EventType} ignored for {ScenarioId}; waiting for {Expected}.",
                eventType,
                scenarioId,
                expected);
            return null;
        }

        var def = _catalog.Get(scenarioId.Trim());
        if (def?.Flow == null)
        {
            _logger.LogWarning("Scenario flow domain event for unknown scenario {ScenarioId}.", scenarioId);
            return null;
        }

        _logger.LogInformation(
            "Resuming scenario flow {ScenarioId} at {ExecutionNodeId} for domain event {EventType}.",
            scenarioId,
            snapshot.ExecutionNodeId,
            eventType);

        return await _orchestrator
            .ResumeDomainEventAsync(
                scenarioId.Trim(),
                def,
                projectRoot,
                sessionId,
                eventType.Trim(),
                payload,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
