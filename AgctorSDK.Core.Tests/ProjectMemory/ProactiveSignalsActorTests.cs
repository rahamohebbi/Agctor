using AgctorSDK.Core.Adapters;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.ProjectMemory.Companion;
using AgctorSDK.Core.ProjectMemory.Companion.Actors;
using AgctorSDK.Core.ProjectMemory.Orchestration;
using AgctorSDK.Core.ProjectMemory.OutOfSchema;
using AgctorSDK.Core.Sessions.Models;
using FluentAssertions;
using Xunit;

namespace AgctorSDK.Core.Tests.ProjectMemory;

public sealed class ProactiveSignalsActorTests
{
    [Fact]
    public async Task ReceiveAsync_Returns_Scan_Results()
    {
        var root = Path.Combine(Path.GetTempPath(), "agctor-proactive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "scenarios", "s1", "people", "ryan"));
        File.WriteAllText(
            Path.Combine(root, "scenarios", "s1", "people", "ryan", "profile.md"),
            "- birthday: 2010-05-20\n");

        var actor = new ProactiveSignalsActor("signals");
        await actor.InitializeAsync();
        var envelope = AgctorEnvelopeBuilder.Request(
            new ProactiveSignalsWorkflowRequest(root, "s1", 30, 14),
            senderId: "test",
            receiverId: actor.Id,
            correlationId: Guid.NewGuid().ToString("N"));

        var response = await actor.ReceiveAsync(envelope);
        response.GetMessageType().Should().Be(AgctorMessageTypes.Result);
        var payload = response.Payload.Should().BeOfType<ProactiveSignalsWorkflowResult>().Subject;
        payload.Signals.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Facade_Routes_Through_Runtime()
    {
        var root = Path.Combine(Path.GetTempPath(), "agctor-proactive-facade-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "scenarios", "s1", "people", "ryan"));
        File.WriteAllText(
            Path.Combine(root, "scenarios", "s1", "people", "ryan", "profile.md"),
            "- birthday: 2010-05-20\n");

        using var runtime = new InMemoryActorRuntime();
        await runtime.InitializeAsync(new Dictionary<string, object>());
        var facade = new ActorBackedCompanionMemoryServices(runtime, new StubSessionStore(), new NoOpPipeline());

        var signals = await facade.ScanAsync(root, "s1");
        signals.Should().NotBeEmpty();
    }

    private sealed class StubSessionStore : ISessionStore
    {
        public Task<SessionInfo> CreateSessionAsync(string? sessionId = null, string? title = null, string? projectId = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public Task<SessionInfo?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<SessionInfo?>(null);
        public Task<SessionInfo> UpdateSessionTitleAsync(string sessionId, string title, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<SessionInfo>> ListSessionsAsync(int limit = 50, int offset = 0, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SessionInfo>>(Array.Empty<SessionInfo>());
        public Task<IReadOnlyList<SessionInfo>> ListSessionsByProjectAsync(string projectId, int limit = 50, int offset = 0, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public Task<IReadOnlyList<SessionInfo>> ListStandaloneSessionsAsync(int limit = 50, int offset = 0, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public Task<IReadOnlyList<SessionTurn>> GetTurnsAsync(string sessionId, int? lastTurns = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SessionTurn>>(Array.Empty<SessionTurn>());
        public Task<SessionTurn> AppendTurnAsync(SessionTurn turn, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public Task<IReadOnlyList<SessionTraceLink>> GetTraceLinksAsync(string sessionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SessionTraceLink>>(Array.Empty<SessionTraceLink>());
        public Task<SessionTraceLink?> GetTraceLinkByTurnIdAsync(string sessionId, string turnId, CancellationToken cancellationToken = default) =>
            Task.FromResult<SessionTraceLink?>(null);
        public Task<SessionTraceLink> UpsertTraceLinkAsync(SessionTraceLink traceLink, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public Task<SessionSummary?> GetSummaryAsync(string sessionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<SessionSummary?>(null);
        public Task UpsertSummaryAsync(SessionSummary summary, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<SessionProject> CreateProjectAsync(string? projectId = null, string? name = null, string? scenarioId = null, string? focusEntityKey = null, string? focusDisplayName = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public Task<SessionProject?> GetProjectAsync(string projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult<SessionProject?>(null);
        public Task<IReadOnlyList<SessionProject>> ListProjectsAsync(int limit = 50, int offset = 0, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public Task<SessionProject> UpdateProjectAsync(SessionProject project, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public Task DeleteProjectAsync(string projectId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public Task AssignSessionToProjectAsync(string sessionId, string projectId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public Task DetachSessionFromProjectAsync(string sessionId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    private sealed class NoOpPipeline : IProjectMemoryPipelineRunner
    {
        public Task<ProjectMemoryPipelineResult> RunAsync(ProjectMemoryPipelineRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProjectMemoryPipelineResult());

        public Task<ProjectMemoryIngestResult> IngestFromExtractorOutputAsync(
            string projectRoot, string? scenarioId, string rawExtractorLlmText, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<GenericInboxPersistResult> PersistApprovedGenericFactsAsync(
            string projectRoot, string? scenarioId, IReadOnlyList<ApprovedGenericFact> approvals, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }
}
