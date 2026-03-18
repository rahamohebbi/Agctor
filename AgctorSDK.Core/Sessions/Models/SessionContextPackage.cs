using System.Collections.Generic;

namespace AgctorSDK.Core.Sessions.Models
{
    /// <summary>
    /// Prompt-ready context built from session history.
    /// </summary>
    public sealed class SessionContextPackage
    {
        public string SessionId { get; set; } = string.Empty;
        public string CurrentPrompt { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public IReadOnlyList<SessionTurn> RecentTurns { get; set; } = new List<SessionTurn>();
        public string PromptContext { get; set; } = string.Empty;
    }
}
