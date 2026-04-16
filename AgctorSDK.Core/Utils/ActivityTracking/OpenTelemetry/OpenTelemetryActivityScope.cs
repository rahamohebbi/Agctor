using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using OpenTelemetry.Trace;

namespace AgctorSDK.Core.Utils.ActivityTracking.OpenTelemetry
{
    /// <summary>
    /// Implementation of IActivityScope that wraps an OpenTelemetry span.
    /// </summary>
    public class OpenTelemetryActivityScope : IActivityScope
    {
        private readonly TelemetrySpan _span;
        
        /// <summary>
        /// Initializes a new instance of the OpenTelemetryActivityScope class.
        /// </summary>
        /// <param name="span">The OpenTelemetry span to wrap.</param>
        public OpenTelemetryActivityScope(TelemetrySpan span)
        {
            _span = span;
        }
        
        /// <inheritdoc/>
        public void SetAttribute(string key, string value)
        {
            _span.SetAttribute(key, value);
        }
        
        /// <inheritdoc/>
        public void SetStatus(ActivityStatus status, string? description = null)
        {
            if (status == ActivityStatus.Ok)
            {
                _span.SetStatus(Status.Ok);
            }
            else
            {
                _span.SetStatus(Status.Error);
                if (!string.IsNullOrEmpty(description))
                {
                    _span.SetAttribute("error.description", description);
                }
            }
        }
        
        /// <inheritdoc/>
        public void RecordEvent(string name, IReadOnlyDictionary<string, object>? attributes = null)
        {
            // Convert attributes to key-value pairs if provided
            if (attributes != null && attributes.Count > 0)
            {
                var timestamp = DateTime.UtcNow;
                var activity = new Activity(name);
                
                foreach (var kvp in attributes)
                {
                    activity.AddTag(kvp.Key, kvp.Value);
                }
                
                _span.AddEvent(name, timestamp);
            }
            else
            {
                _span.AddEvent(name);
            }
        }
        
        /// <inheritdoc/>
        public void RecordException(Exception exception)
        {
            _span.RecordException(exception);
        }

        /// <inheritdoc/>
        public void SetTimelineDetailJson(string? json)
        {
            if (string.IsNullOrEmpty(json))
                return;
            const int max = 4096;
            var v = json.Length <= max ? json : json.Substring(0, max);
            _span.SetAttribute("agctor.timeline.detail", v);
        }
        
        /// <inheritdoc/>
        public void Dispose()
        {
            _span.Dispose();
        }
    }
} 