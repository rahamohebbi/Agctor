using System.Collections.Generic;
using AgctorSDK.Core.Sessions.Models;

namespace AgctorSDK.Core.Sessions.Messages
{
    /// <summary>
    /// Creates a session or ensures one exists.
    /// </summary>
    public sealed class CreateSessionMessage
    {
        public string? SessionId { get; set; }
        public string? Title { get; set; }
    }

    /// <summary>
    /// Lists known sessions.
    /// </summary>
    public sealed class ListSessionsMessage
    {
        public int Limit { get; set; } = 50;
        public int Offset { get; set; }
    }

    /// <summary>
    /// Loads a transcript for a single session.
    /// </summary>
    public sealed class GetSessionTranscriptMessage
    {
        public string SessionId { get; set; } = string.Empty;
        public int? LastTurns { get; set; }
    }

    /// <summary>
    /// Appends one user/assistant/system/tool turn to a session.
    /// </summary>
    public sealed class AppendSessionTurnMessage
    {
        public SessionTurn Turn { get; set; } = new();
    }

    /// <summary>
    /// Upserts durable trace lookup metadata for one logical chat turn.
    /// </summary>
    public sealed class UpsertSessionTraceLinkMessage
    {
        public SessionTraceLink TraceLink { get; set; } = new();
    }

    /// <summary>
    /// Loads trace link metadata for one session.
    /// </summary>
    public sealed class GetSessionTraceLinksMessage
    {
        public string SessionId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Loads a trace link by either request or response turn id.
    /// </summary>
    public sealed class GetSessionTraceLinkByTurnMessage
    {
        public string SessionId { get; set; } = string.Empty;
        public string TurnId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Builds a prompt context package from an existing session.
    /// </summary>
    public sealed class GetSessionContextMessage
    {
        public string SessionId { get; set; } = string.Empty;
        public string CurrentPrompt { get; set; } = string.Empty;
    }

    /// <summary>
    /// Generic response wrapper for session list operations.
    /// </summary>
    public sealed class SessionListResult
    {
        public IReadOnlyList<SessionInfo> Sessions { get; set; } = new List<SessionInfo>();
    }
}
