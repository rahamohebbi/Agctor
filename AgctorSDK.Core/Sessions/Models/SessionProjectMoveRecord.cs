using System;

namespace AgctorSDK.Core.Sessions.Models
{
    /// <summary>
    /// Audit row for moving a session between project buckets.
    /// </summary>
    public sealed class SessionProjectMoveRecord
    {
        public string MoveId { get; set; } = Guid.NewGuid().ToString();
        public string SessionId { get; set; } = string.Empty;
        public string? FromProjectId { get; set; }
        public string? ToProjectId { get; set; }
        public DateTimeOffset MovedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
