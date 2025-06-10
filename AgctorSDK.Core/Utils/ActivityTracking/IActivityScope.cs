using System;
using System.Collections.Generic;

namespace AgctorSDK.Core.Utils.ActivityTracking
{
    /// <summary>
    /// Represents a single traceable activity/span that can be enriched with attributes,
    /// events, and status information. Implements IDisposable to allow using statement
    /// for automatic completion of the activity.
    /// </summary>
    public interface IActivityScope : IDisposable
    {
        /// <summary>
        /// Sets an attribute (key-value pair) on the activity.
        /// </summary>
        /// <param name="key">The attribute key.</param>
        /// <param name="value">The attribute value.</param>
        void SetAttribute(string key, string value);

        /// <summary>
        /// Sets the status of the activity.
        /// </summary>
        /// <param name="status">The activity status (Ok or Error).</param>
        /// <param name="description">Optional description providing more details about the status.</param>
        void SetStatus(ActivityStatus status, string? description = null);

        /// <summary>
        /// Records a named event within the activity with optional attributes.
        /// </summary>
        /// <param name="name">The name of the event.</param>
        /// <param name="attributes">Optional attributes providing more context for the event.</param>
        void RecordEvent(string name, IReadOnlyDictionary<string, object>? attributes = null);

        /// <summary>
        /// Records an exception that occurred during the activity.
        /// </summary>
        /// <param name="exception">The exception to record.</param>
        void RecordException(Exception exception);
    }
} 