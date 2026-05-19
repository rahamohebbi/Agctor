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

public sealed class SessionEndIngestActorTests
{
    [Fact]
    public async Task RunAsync_Skips_When_No_New_Turns()
    {
        var store = new FakeSessionStore();
        var session = await store.CreateSessionAsync(projectId: "proj-1");
        await store.CreateProjectAsync("proj-1", "People", "person_3");
        await store.AppendTurnAsync(new SessionTurn
        {
            SessionId = session.SessionId,
            Sequence = 1,
            Role = SessionRole.User,
            Content = "hello"
        });
        await store.UpsertSummaryAsync(new SessionSummary
        {
            SessionId = session.SessionId,
            Content = "done",
            LastIncludedSequence = 1
        });

        var actor = new SessionEndIngestActor("test", store, new NoOpPipelineRunner());
        await actor.InitializeAsync();
        var result = await SendIngestAsync(actor,
            new SessionEndIngestWorkflowRequest(session.SessionId, "/tmp/root", null, SessionEndIngestTrigger.Checkpoint));

        result.Skipped.Should().BeTrue();
        result.SkipReason.Should().Be("no_new_turns");
    }

    [Fact]
    public async Task RunAsync_Ingests_New_Turns_And_Updates_Summary_Cursor()
    {
        var store = new FakeSessionStore();
        var session = await store.CreateSessionAsync(projectId: "proj-1");
        await store.CreateProjectAsync("proj-1", "People", "person_3");
        await store.AppendTurnAsync(new SessionTurn
        {
            SessionId = session.SessionId,
            Sequence = 1,
            Role = SessionRole.User,
            Content = "Ryan likes soccer"
        });
        await store.AppendTurnAsync(new SessionTurn
        {
            SessionId = session.SessionId,
            Sequence = 2,
            Role = SessionRole.Assistant,
            Content = "Noted"
        });

        var runner = new RecordingPipelineRunner();
        var actor = new SessionEndIngestActor("test", store, runner);
        await actor.InitializeAsync();
        var result = await SendIngestAsync(actor,
            new SessionEndIngestWorkflowRequest(session.SessionId, "/tmp/root", null, SessionEndIngestTrigger.Delete));

        result.Skipped.Should().BeFalse();
        result.Success.Should().BeTrue();
        result.LastIncludedSequence.Should().Be(2);
        runner.LastRequest.Should().NotBeNull();
        runner.LastRequest!.Mode.Should().Be(ProjectMemoryPipelineMode.IngestOnly);
        runner.LastRequest.ScenarioId.Should().Be("person_3");
        runner.LastRequest.UserMessage.Should().Contain("Ryan likes soccer");

        var summary = await store.GetSummaryAsync(session.SessionId);
        summary.Should().NotBeNull();
        summary!.LastIncludedSequence.Should().Be(2);
        summary.Content.Should().Be("ingested");
    }

    private static async Task<SessionEndIngestWorkflowResult> SendIngestAsync(
        SessionEndIngestActor actor,
        SessionEndIngestWorkflowRequest request)
    {
        var envelope = AgctorEnvelopeBuilder.Request(
            request,
            senderId: "test",
            receiverId: actor.Id,
            correlationId: Guid.NewGuid().ToString("N"));
        var response = await actor.ReceiveAsync(envelope);
        response.GetMessageType().Should().Be(AgctorMessageTypes.Result);
        return response.Payload.Should().BeOfType<SessionEndIngestWorkflowResult>().Subject;
    }

    private sealed class NoOpPipelineRunner : IProjectMemoryPipelineRunner
    {
        public Task<ProjectMemoryPipelineResult> RunAsync(ProjectMemoryPipelineRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProjectMemoryPipelineResult { Success = true, CorrelationId = "x" });

        public Task<ProjectMemoryIngestResult> IngestFromExtractorOutputAsync(
            string projectRoot, string? scenarioId, string rawExtractorLlmText, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<GenericInboxPersistResult> PersistApprovedGenericFactsAsync(
            string projectRoot, string? scenarioId, IReadOnlyList<ApprovedGenericFact> approvals, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    private sealed class RecordingPipelineRunner : IProjectMemoryPipelineRunner
    {
        public ProjectMemoryPipelineRequest? LastRequest { get; private set; }

        public Task<ProjectMemoryPipelineResult> RunAsync(ProjectMemoryPipelineRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new ProjectMemoryPipelineResult
            {
                Success = true,
                CorrelationId = "corr-1",
                FinalText = "ingested"
            });
        }

        public Task<ProjectMemoryIngestResult> IngestFromExtractorOutputAsync(
            string projectRoot, string? scenarioId, string rawExtractorLlmText, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<GenericInboxPersistResult> PersistApprovedGenericFactsAsync(
            string projectRoot, string? scenarioId, IReadOnlyList<ApprovedGenericFact> approvals, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    private sealed class FakeSessionStore : ISessionStore
    {
        private readonly Dictionary<string, SessionInfo> _sessions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<SessionTurn>> _turns = new(StringComparer.Ordinal);
        private readonly Dictionary<string, SessionSummary> _summaries = new(StringComparer.Ordinal);
        private readonly Dictionary<string, SessionProject> _projects = new(StringComparer.Ordinal);

        public Task<SessionInfo> CreateSessionAsync(string? sessionId = null, string? title = null, string? projectId = null, CancellationToken cancellationToken = default)
        {
            var id = sessionId ?? Guid.NewGuid().ToString("N");
            var info = new SessionInfo { SessionId = id, Title = title ?? "chat", ProjectId = projectId };
            _sessions[id] = info;
            _turns[id] = new List<SessionTurn>();
            return Task.FromResult(info);
        }

        public Task<SessionInfo?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_sessions.TryGetValue(sessionId, out var s) ? s : null);

        public Task<SessionInfo> UpdateSessionTitleAsync(string sessionId, string title, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            _sessions.Remove(sessionId);
            _turns.Remove(sessionId);
            _summaries.Remove(sessionId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SessionInfo>> ListSessionsAsync(int limit = 50, int offset = 0, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SessionInfo>>(_sessions.Values.ToList());

        public Task<IReadOnlyList<SessionInfo>> ListSessionsByProjectAsync(string projectId, int limit = 50, int offset = 0, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<SessionInfo>> ListStandaloneSessionsAsync(int limit = 50, int offset = 0, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<SessionTurn>> GetTurnsAsync(string sessionId, int? lastTurns = null, CancellationToken cancellationToken = default)
        {
            _turns.TryGetValue(sessionId, out var list);
            return Task.FromResult<IReadOnlyList<SessionTurn>>((list ?? new List<SessionTurn>()).AsReadOnly());
        }

        public Task<SessionTurn> AppendTurnAsync(SessionTurn turn, CancellationToken cancellationToken = default)
        {
            if (!_turns.TryGetValue(turn.SessionId, out var list))
            {
                list = new List<SessionTurn>();
                _turns[turn.SessionId] = list;
            }

            list.Add(turn);
            return Task.FromResult(turn);
        }

        public Task<IReadOnlyList<SessionTraceLink>> GetTraceLinksAsync(string sessionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SessionTraceLink>>(Array.Empty<SessionTraceLink>());

        public Task<SessionTraceLink?> GetTraceLinkByTurnIdAsync(string sessionId, string turnId, CancellationToken cancellationToken = default) =>
            Task.FromResult<SessionTraceLink?>(null);

        public Task<SessionTraceLink> UpsertTraceLinkAsync(SessionTraceLink traceLink, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<SessionSummary?> GetSummaryAsync(string sessionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_summaries.TryGetValue(sessionId, out var s) ? s : null);

        public Task UpsertSummaryAsync(SessionSummary summary, CancellationToken cancellationToken = default)
        {
            _summaries[summary.SessionId] = summary;
            return Task.CompletedTask;
        }

        public Task<SessionProject> CreateProjectAsync(
            string? projectId = null,
            string? name = null,
            string? scenarioId = null,
            string? focusEntityKey = null,
            string? focusDisplayName = null,
            CancellationToken cancellationToken = default)
        {
            var id = projectId ?? Guid.NewGuid().ToString("N");
            var project = new SessionProject { ProjectId = id, Name = name ?? "p", ScenarioId = scenarioId ?? "people" };
            _projects[id] = project;
            return Task.FromResult(project);
        }

        public Task<SessionProject?> GetProjectAsync(string projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_projects.TryGetValue(projectId, out var p) ? p : null);

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
}
