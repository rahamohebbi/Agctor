using System;

namespace AgctorSDK.Core.Sessions.Models
{
    /// <summary>
    /// Rolling summary stored per session for long-running chats.
    /// </summary>
    public sealed class SessionSummary
    {
        public string SessionId { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public int LastIncludedSequence { get; set; }
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
