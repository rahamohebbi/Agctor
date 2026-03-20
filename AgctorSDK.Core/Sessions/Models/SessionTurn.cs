using System;

namespace AgctorSDK.Core.Sessions.Models
{
    /// <summary>
    /// Single immutable turn in a session.
    /// </summary>
    public sealed class SessionTurn
    {
        public string TurnId { get; set; } = Guid.NewGuid().ToString();
        /// <summary>
        /// Logical chat turn shared by a user prompt and its assistant response.
        /// Keeps request/response drill-down tied to one parent turn.
        /// </summary>
        public string TurnGroupId { get; set; } = Guid.NewGuid().ToString();
        public string SessionId { get; set; } = string.Empty;
        public int Sequence { get; set; }
        public SessionRole Role { get; set; }
        public string Content { get; set; } = string.Empty;
        public string? AgentId { get; set; }
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
