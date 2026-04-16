using System;

namespace AgctorSDK.Core.Utils.Observability.Visualization
{
    /// <summary>
    /// Interface for defining activity in the system.
    /// </summary>
    public interface IActivity
    {
        /// <summary>
        /// Gets the ID of the activity.
        /// </summary>
        string Id { get; }
        
        /// <summary>
        /// Gets the parent activity ID.
        /// </summary>
        string? ParentId { get; }
        
        /// <summary>
        /// Gets the name of the activity.
        /// </summary>
        string? Name { get; }
        
        /// <summary>
        /// Gets the display name of the activity.
        /// </summary>
        string? DisplayName { get; }
        
        /// <summary>
        /// Gets the duration of the activity.
        /// </summary>
        TimeSpan Duration { get; }
        
        /// <summary>
        /// Gets a value indicating whether the activity has a result.
        /// </summary>
        bool HasResult { get; }
        
        /// <summary>
        /// Gets the trace ID of the activity.
        /// </summary>
        string TraceId { get; }
        
        /// <summary>
        /// Gets the timestamp of the activity.
        /// </summary>
        DateTimeOffset Timestamp { get; }

        /// <summary>Optional JSON payload for dashboard drill-down (producer-defined shape, e.g. playground LLM I/O).</summary>
        string? TimelineDetailJson { get; }
    }
} 