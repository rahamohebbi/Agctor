using AgctorSDK.Core.ProjectMemory.Scenarios;
using AgctorSDK.Core.ProjectMemory.Scenarios.Messages;
using AgctorSDK.Host.Models;
using AgctorSDK.Host.Services.Scenarios;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgctorSDK.Host.IntegrationTests;

/// <summary>PRD-024 Phase C: domain events resume flows suspended at <c>AwaitEvent</c>.</summary>
public sealed class ScenarioFlowDomainEventPublisherTests
{
    [Fact]
    public async Task TryResumeAsync_noops_when_snapshot_not_waiting_for_domain_event()
    {
        var store = new InMemoryScenarioFlowRuntimeStore();
        var orchestrator = new RecordingOrchestrator();
        var catalog = new StubScenarioCatalog();
        var publisher = new ScenarioFlowDomainEventPublisher(
            store,
            catalog,
            orchestrator,
            NullLogger<ScenarioFlowDomainEventPublisher>.Instance);

        var result = await publisher.TryResumeAsync(
            "/tmp/p",
            "sess-1",
            "people-style-photo-loop",
            ScenarioFlowDomainEventTypes.VisualExtractCompleted,
            new Dictionary<string, object?> { ["visual.hasPhotos"] = true });

        result.Should().BeNull();
        orchestrator.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task TryResumeAsync_ignores_mismatched_event_type()
    {
        var store = new InMemoryScenarioFlowRuntimeStore();
        await store.SaveAsync("/tmp/p", "sess-1", "people-style-photo-loop", new ScenarioFlowRuntimeSnapshot
        {
            Status = ScenarioFlowRuntimeStatus.WaitingForDomainEvent,
            ExecutionNodeId = "n_await",
            AwaitingEvent = new ScenarioFlowAwaitingEventState { EventType = ScenarioFlowDomainEventTypes.VisualExtractCompleted }
        });

        var orchestrator = new RecordingOrchestrator();
        var catalog = new StubScenarioCatalog();
        var publisher = new ScenarioFlowDomainEventPublisher(
            store,
            catalog,
            orchestrator,
            NullLogger<ScenarioFlowDomainEventPublisher>.Instance);

        var result = await publisher.TryResumeAsync(
            "/tmp/p",
            "sess-1",
            "people-style-photo-loop",
            ScenarioFlowDomainEventTypes.InboxConfirmed,
            new Dictionary<string, object?>());

        result.Should().BeNull();
        orchestrator.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task TryResumeAsync_delegates_to_orchestrator_when_event_matches()
    {
        var store = new InMemoryScenarioFlowRuntimeStore();
        await store.SaveAsync("/tmp/p", "sess-1", "people-style-photo-loop", new ScenarioFlowRuntimeSnapshot
        {
            Status = ScenarioFlowRuntimeStatus.WaitingForDomainEvent,
            ExecutionNodeId = "n_await",
            AwaitingEvent = new ScenarioFlowAwaitingEventState { EventType = ScenarioFlowDomainEventTypes.VisualExtractCompleted }
        });

        var orchestrator = new RecordingOrchestrator
        {
            NextResult = new ScenarioFlowRuntimeResult(true, true, ScenarioFlowRuntimeStatus.Completed, "out1", "Done.", null, null)
        };
        var catalog = new StubScenarioCatalog();
        var publisher = new ScenarioFlowDomainEventPublisher(
            store,
            catalog,
            orchestrator,
            NullLogger<ScenarioFlowDomainEventPublisher>.Instance);

        var payload = new Dictionary<string, object?> { ["visual.hasPhotos"] = true };
        var result = await publisher.TryResumeAsync(
            "/tmp/p",
            "sess-1",
            "people-style-photo-loop",
            ScenarioFlowDomainEventTypes.VisualExtractCompleted,
            payload);

        result.Should().NotBeNull();
        result!.Completed.Should().BeTrue();
        orchestrator.Calls.Should().ContainSingle();
        orchestrator.Calls[0].EventType.Should().Be(ScenarioFlowDomainEventTypes.VisualExtractCompleted);
        orchestrator.Calls[0].Payload.Should().ContainKey("visual.hasPhotos");
    }

    private sealed class InMemoryScenarioFlowRuntimeStore : IScenarioFlowRuntimeStore
    {
        private readonly Dictionary<string, ScenarioFlowRuntimeSnapshot> _data = new();

        private static string Key(string projectRoot, string sessionId, string scenarioId) =>
            $"{projectRoot}|{sessionId}|{scenarioId}";

        public Task<ScenarioFlowRuntimeSnapshot?> LoadAsync(string projectRoot, string sessionId, string scenarioId, CancellationToken cancellationToken = default)
        {
            _data.TryGetValue(Key(projectRoot, sessionId, scenarioId), out var snap);
            return Task.FromResult(snap);
        }

        public Task SaveAsync(string projectRoot, string sessionId, string scenarioId, ScenarioFlowRuntimeSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            _data[Key(projectRoot, sessionId, scenarioId)] = snapshot;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string projectRoot, string sessionId, string scenarioId, CancellationToken cancellationToken = default)
        {
            _data.Remove(Key(projectRoot, sessionId, scenarioId));
            return Task.CompletedTask;
        }
    }

    private sealed class StubScenarioCatalog : IScenarioCatalog
    {
        public ScenarioDefinition? Get(string scenarioId) =>
            string.Equals(scenarioId, "people-style-photo-loop", StringComparison.OrdinalIgnoreCase)
                ? new ScenarioDefinition
                {
                    Id = "people-style-photo-loop",
                    Flow = new ScenarioFlowDocument { GraphId = "people-style-photo-loop", Nodes = [new ScenarioFlowNode { Id = "in1", Type = "ChatInput" }] }
                }
                : null;

        public IReadOnlyList<ScenarioDefinition> List() => Array.Empty<ScenarioDefinition>();

        public IReadOnlyList<string> GetSuppressedDefaultScenarioIds() => Array.Empty<string>();

        public Task ReloadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<(bool Ok, IReadOnlyList<string> Errors)> SaveAsync(ScenarioCatalogDocument userDocument, CancellationToken cancellationToken = default) =>
            Task.FromResult((true, (IReadOnlyList<string>)Array.Empty<string>()));

        public Task<(bool Ok, IReadOnlyList<string> Errors)> SaveScenarioFlowAsync(string scenarioId, ScenarioFlowDocument? flow, CancellationToken cancellationToken = default) =>
            Task.FromResult((true, (IReadOnlyList<string>)Array.Empty<string>()));

        public Task<(bool Ok, IReadOnlyList<string> Errors)> CreateScenarioAsync(ScenarioDefinition scenario, CancellationToken cancellationToken = default) =>
            Task.FromResult((true, (IReadOnlyList<string>)Array.Empty<string>()));

        public Task<(bool Ok, IReadOnlyList<string> Errors)> DeleteScenarioAsync(string scenarioId, CancellationToken cancellationToken = default) =>
            Task.FromResult((true, (IReadOnlyList<string>)Array.Empty<string>()));
    }

    private sealed class RecordingOrchestrator : IScenarioFlowRuntimeOrchestrator
    {
        public List<(string EventType, IReadOnlyDictionary<string, object?> Payload)> Calls { get; } = new();
        public ScenarioFlowRuntimeResult NextResult { get; set; } =
            new(true, false, ScenarioFlowRuntimeStatus.Running, string.Empty, null, null, null);

        public Task<ScenarioFlowRuntimeResult> RunAsync(
            string scenarioId,
            ScenarioDefinition definition,
            ScenarioFlowRunRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(NextResult);

        public Task<ScenarioFlowRuntimeResult> ResumeDomainEventAsync(
            string scenarioId,
            ScenarioDefinition definition,
            string projectRoot,
            string sessionId,
            string eventType,
            IReadOnlyDictionary<string, object?> payload,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((eventType, payload));
            return Task.FromResult(NextResult);
        }

        public Task<ScenarioFlowRuntimeResult?> TryAdvanceStuckPhotoCollectionAsync(
            string scenarioId,
            ScenarioDefinition definition,
            string projectRoot,
            string sessionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ScenarioFlowRuntimeResult?>(null);
    }
}
