using System.Collections.Generic;

namespace AgctorSDK.Core.Sessions.Models
{
    /// <summary>
    /// Session metadata plus full or partial turn history.
    /// </summary>
    public sealed class SessionTranscript
    {
        public SessionInfo Session { get; set; } = new();
        public IReadOnlyList<SessionTurn> Turns { get; set; } = new List<SessionTurn>();
        public IReadOnlyList<SessionTraceLink> TraceLinks { get; set; } = new List<SessionTraceLink>();
        public SessionSummary? Summary { get; set; }
    }
}
