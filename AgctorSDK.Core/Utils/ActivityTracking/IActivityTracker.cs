using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AgctorSDK.Core.Utils.Observability.Visualization;

namespace AgctorSDK.Core.Utils.ActivityTracking
{
    /// <summary>
    /// Core abstraction for activity tracking that enables tracing of operations
    /// across the actor system without coupling to specific tracing implementations.
    /// </summary>
    public interface IActivityTracker
    {
        /// <summary>
        /// Starts a new activity with the given name and optional parent context.
        /// </summary>
        /// <param name="name">The name of the activity to start.</param>
        /// <param name="context">Optional context from a parent activity.</param>
        /// <returns>An activity scope that should be disposed when the activity completes.</returns>
        IActivityScope StartActivity(string name, IReadOnlyDictionary<string, string>? context = null);

        /// <summary>
        /// Propagates the current activity context to the given headers dictionary.
        /// Used for passing context between actors via messages.
        /// </summary>
        /// <param name="headers">The headers dictionary to populate with context.</param>
        void PropagateContext(IDictionary<string, string> headers);

        /// <summary>
        /// Extracts the current activity context as a dictionary that can be used
        /// to start a new activity that continues the current one.
        /// </summary>
        /// <returns>A dictionary containing the current activity context.</returns>
        IDictionary<string, string> ExtractContext();

        /// <summary>
        /// Gets activities for a specific trace.
        /// </summary>
        /// <param name="traceId">The trace ID to retrieve activities for.</param>
        /// <returns>Collection of activities in the trace.</returns>
        Task<IEnumerable<IActivity>> GetTraceActivitiesAsync(string traceId);
    }

    /// <summary>
    /// Represents the status of an activity.
    /// </summary>
    public enum ActivityStatus
    {
        InProgress,
        Ok,
        Error,
        Completed
    }

    /// <summary>
    /// Context information for an activity.
    /// </summary>
    public class ActivityContext
    {
        /// <summary>
        /// Gets or sets the trace ID.
        /// </summary>
        public string TraceId { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the span ID.
        /// </summary>
        public string SpanId { get; set; } = string.Empty;
    }
} 