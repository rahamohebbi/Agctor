using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Sessions.Models;

namespace AgctorSDK.Core.Interfaces
{
    /// <summary>
    /// Persists session metadata and ordered turns.
    /// </summary>
    public interface ISessionStore
    {
        /// <param name="projectId">Optional chat project bucket; must exist when set.</param>
        Task<SessionInfo> CreateSessionAsync(string? sessionId = null, string? title = null, string? projectId = null, CancellationToken cancellationToken = default);
        Task<SessionInfo?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default);
        /// <summary>Updates the session title (trims whitespace, rejects empty). Returns the latest session metadata.</summary>
        Task<SessionInfo> UpdateSessionTitleAsync(string sessionId, string title, CancellationToken cancellationToken = default);
        /// <summary>Removes a session and all of its turns, trace links, summary, and project moves. No-op if the id is missing.</summary>
        Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<SessionInfo>> ListSessionsAsync(int limit = 50, int offset = 0, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<SessionInfo>> ListSessionsByProjectAsync(string projectId, int limit = 50, int offset = 0, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<SessionInfo>> ListStandaloneSessionsAsync(int limit = 50, int offset = 0, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<SessionTurn>> GetTurnsAsync(string sessionId, int? lastTurns = null, CancellationToken cancellationToken = default);
        Task<SessionTurn> AppendTurnAsync(SessionTurn turn, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<SessionTraceLink>> GetTraceLinksAsync(string sessionId, CancellationToken cancellationToken = default);
        Task<SessionTraceLink?> GetTraceLinkByTurnIdAsync(string sessionId, string turnId, CancellationToken cancellationToken = default);
        Task<SessionTraceLink> UpsertTraceLinkAsync(SessionTraceLink traceLink, CancellationToken cancellationToken = default);
        Task<SessionSummary?> GetSummaryAsync(string sessionId, CancellationToken cancellationToken = default);
        Task UpsertSummaryAsync(SessionSummary summary, CancellationToken cancellationToken = default);
        Task<SessionProject> CreateProjectAsync(
            string? projectId = null,
            string? name = null,
            string? scenarioId = null,
            string? focusEntityKey = null,
            string? focusDisplayName = null,
            CancellationToken cancellationToken = default);
        Task<SessionProject?> GetProjectAsync(string projectId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<SessionProject>> ListProjectsAsync(int limit = 50, int offset = 0, CancellationToken cancellationToken = default);
        Task<SessionProject> UpdateProjectAsync(SessionProject project, CancellationToken cancellationToken = default);
        Task DeleteProjectAsync(string projectId, CancellationToken cancellationToken = default);
        Task AssignSessionToProjectAsync(string sessionId, string projectId, CancellationToken cancellationToken = default);
        Task DetachSessionFromProjectAsync(string sessionId, CancellationToken cancellationToken = default);
    }
}
