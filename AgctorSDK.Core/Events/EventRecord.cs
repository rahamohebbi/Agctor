using System;
using System.Collections.Generic;

namespace AgctorSDK.Core.Events
{
    /// <summary>
    /// Represents a record of an event that occurred within the system.
    /// </summary>
    public class EventRecord
    {
        /// <summary>
        /// Gets or sets the unique identifier for the event.
        /// Defaults to a new GUID.
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Gets or sets the timestamp of when the event occurred.
        /// Defaults to the current UTC time.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Gets or sets the type of the event.
        /// Examples: "RunTestSuite", "CodeGenerated", "PromptEvaluated".
        /// Defaults to "Generic".
        /// </summary>
        public string EventType { get; set; } = "Generic";

        /// <summary>
        /// Gets or sets the identifier of the actor (agent or human) that triggered the event.
        /// </summary>
        public string ActorId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the hash of the related prompt in the PromptStore, if applicable.
        /// </summary>
        public string? RelatedPromptHash { get; set; }

        /// <summary>
        /// Gets or sets the target file path if the event involved a code resource or file.
        /// </summary>
        public string? TargetFile { get; set; }

        /// <summary>
        /// Gets or sets a dictionary for additional metadata related to the event.
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new();
    }
} 