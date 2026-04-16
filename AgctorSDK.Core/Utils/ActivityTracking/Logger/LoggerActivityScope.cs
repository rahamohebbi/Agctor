using System;
using System.Collections.Generic;
using AgctorSDK.Core.Utils.Logging;

namespace AgctorSDK.Core.Utils.ActivityTracking.Logger
{
    /// <summary>
    /// Implementation of IActivityScope that uses the Agctor logging system for activity scoping.
    /// </summary>
    public class LoggerActivityScope : IActivityScope
    {
        private readonly LoggerActivityTracker _tracker;
        private readonly IAgctorLogger _logger;
        private readonly ActivityInfo _activityInfo;
        private bool _disposed;
        
        /// <summary>
        /// Initializes a new instance of the LoggerActivityScope class.
        /// </summary>
        /// <param name="tracker">The parent activity tracker.</param>
        /// <param name="logger">The logger to use for recording activity events.</param>
        /// <param name="activityInfo">Information about the activity.</param>
        public LoggerActivityScope(LoggerActivityTracker tracker, IAgctorLogger logger, ActivityInfo activityInfo)
        {
            _tracker = tracker;
            _logger = logger;
            _activityInfo = activityInfo;
        }
        
        /// <inheritdoc/>
        public void SetAttribute(string key, string value)
        {
            if (string.Equals(key, "display.name", StringComparison.OrdinalIgnoreCase))
            {
                _activityInfo.DisplayName = value;
            }
            _logger.Debug($"Activity [{_activityInfo.Id}] attribute: {key}={value}");
        }
        
        /// <inheritdoc/>
        public void SetStatus(ActivityStatus status, string? description = null)
        {
            _activityInfo.Status = status;
            _logger.Debug($"Activity [{_activityInfo.Id}] status: {status} {description ?? ""}");
        }
        
        /// <inheritdoc/>
        public void RecordEvent(string name, IReadOnlyDictionary<string, object>? attributes = null)
        {
            _activityInfo.DisplayName ??= _activityInfo.Name;
            var attrString = "";
            if (attributes != null && attributes.Count > 0)
            {
                var parts = new List<string>();
                foreach (var kvp in attributes)
                {
                    parts.Add($"{kvp.Key}={kvp.Value}");
                }
                attrString = " " + string.Join(", ", parts);
            }
            
            _logger.Debug($"Activity [{_activityInfo.Id}] event: {name}{attrString}");
        }
        
        /// <inheritdoc/>
        public void RecordException(Exception exception)
        {
            _activityInfo.Status = ActivityStatus.Error;
            _logger.Error(exception, $"Activity [{_activityInfo.Id}] exception: {exception.Message}");
        }

        /// <inheritdoc/>
        public void SetTimelineDetailJson(string? json)
        {
            _activityInfo.TimelineDetailJson = json;
        }
        
        /// <inheritdoc/>
        public void Dispose()
        {
            if (!_disposed)
            {
                if (_activityInfo.Status == ActivityStatus.InProgress)
                {
                    _activityInfo.Status = ActivityStatus.Completed;
                }
                _tracker.EndActivity(_activityInfo);
                _disposed = true;
            }
        }
    }
} 