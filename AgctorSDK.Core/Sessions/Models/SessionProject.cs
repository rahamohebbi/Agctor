using System;

namespace AgctorSDK.Core.Sessions.Models
{
    /// <summary>
    /// Project bucket that can contain many chat sessions.
    /// </summary>
    public sealed class SessionProject
    {
        public string ProjectId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        /// <summary>Catalog id from /api/scenarios, e.g. "people".</summary>
        public string ScenarioId { get; set; } = "people";
        /// <summary>Primary person for this project (entity folder slug under scenario people/).</summary>
        public string? FocusEntityKey { get; set; }
        /// <summary>Display label for <see cref="FocusEntityKey"/> shown in UI.</summary>
        public string? FocusDisplayName { get; set; }
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
        public int SessionCount { get; set; }
    }
}
