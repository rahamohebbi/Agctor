using System;

namespace AgctorSDK.Core.Sessions.Models
{
    /// <summary>
    /// Durable lookup metadata that links a logical chat turn and its messages to trace identifiers.
    /// Trace payloads live elsewhere; this record lets the UI discover what to load.
    /// </summary>
    public sealed class SessionTraceLink
    {
        public string TraceLinkId { get; set; } = Guid.NewGuid().ToString();
        public string SessionId { get; set; } = string.Empty;
        public string TurnGroupId { get; set; } = string.Empty;
        public string RequestTurnId { get; set; } = string.Empty;
        public string? ResponseTurnId { get; set; }
        public string? PrimaryTraceId { get; set; }
        public string? RequestTraceId { get; set; }
        public string? ResponseTraceId { get; set; }
        public string? AgentId { get; set; }
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
