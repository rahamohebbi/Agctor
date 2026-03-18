using System;

namespace AgctorSDK.Core.Sessions.Models
{
    /// <summary>
    /// Session metadata used for listing and selection.
    /// </summary>
    public sealed class SessionInfo
    {
        public string SessionId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
        public int TurnCount { get; set; }
    }
}
