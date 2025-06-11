using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using AgctorSDK.Core.Utils.Logging;
using AgctorSDK.Core.Utils.Observability.Visualization;

namespace AgctorSDK.Core.Utils.ActivityTracking.Logger
{
    /// <summary>
    /// Implementation of IActivityTracker that uses the Agctor logging system for activity tracking.
    /// This provides a lightweight alternative when OpenTelemetry is not available or desired.
    /// </summary>
    public class LoggerActivityTracker : IActivityTracker
    {
        private readonly IAgctorLogger _logger;
        private readonly AsyncLocal<Stack<ActivityInfo>> _activityStack = new AsyncLocal<Stack<ActivityInfo>>();
        
        /// <summary>
        /// Initializes a new instance of the LoggerActivityTracker class.
        /// </summary>
        /// <param name="logger">The logger to use for recording activities.</param>
        public LoggerActivityTracker(IAgctorLogger logger)
        {
            _logger = logger;
        }

        private Stack<ActivityInfo> ActivityStack => 
            _activityStack.Value ??= new Stack<ActivityInfo>();

        /// <inheritdoc/>
        public IActivityScope StartActivity(string name, IReadOnlyDictionary<string, string>? context = null)
        {
            string? parentId = null;
            if (context != null && context.TryGetValue("activity-id", out var pid))
            {
                parentId = pid;
            }

            var activityId = Guid.NewGuid().ToString();
            var activityInfo = new ActivityInfo
            {
                Id = activityId,
                Name = name,
                ParentId = parentId ?? (ActivityStack.Count > 0 ? ActivityStack.Peek().Id : null),
                StartTime = DateTime.UtcNow
            };

            ActivityStack.Push(activityInfo);
            
            _logger.Info($"Activity started: {name} [ID: {activityId}, Parent: {activityInfo.ParentId ?? "none"}]");
            
            return new LoggerActivityScope(this, _logger, activityInfo);
        }

        /// <inheritdoc/>
        public void PropagateContext(IDictionary<string, string> headers)
        {
            if (ActivityStack.Count > 0)
            {
                headers["activity-id"] = ActivityStack.Peek().Id;
            }
        }

        /// <inheritdoc/>
        public IDictionary<string, string> ExtractContext()
        {
            var context = new Dictionary<string, string>();
            if (ActivityStack.Count > 0)
            {
                context["activity-id"] = ActivityStack.Peek().Id;
            }
            return context;
        }
        
        /// <inheritdoc/>
        public Task<IEnumerable<IActivity>> GetTraceActivitiesAsync(string traceId)
        {
            _logger.Info($"GetTraceActivitiesAsync called for traceId: {traceId}");
            
            // In a real implementation, this would retrieve activities from a log storage
            // For now, we return an empty collection as this is a simple logger-based implementation
            _logger.Warning("GetTraceActivitiesAsync is not fully implemented in LoggerActivityTracker");
            
            return Task.FromResult<IEnumerable<IActivity>>(new List<IActivity>());
        }

        /// <summary>
        /// Ends an activity and updates logging information.
        /// </summary>
        /// <param name="activityInfo">The activity information.</param>
        internal void EndActivity(ActivityInfo activityInfo)
        {
            if (ActivityStack.Count > 0 && ActivityStack.Peek().Id == activityInfo.Id)
            {
                ActivityStack.Pop();
                
                var duration = DateTime.UtcNow - activityInfo.StartTime;
                _logger.Info($"Activity completed: {activityInfo.Name} [ID: {activityInfo.Id}] in {duration.TotalMilliseconds:F2}ms");
            }
        }
    }

    /// <summary>
    /// Represents information about an activity for tracking purposes.
    /// </summary>
    public class ActivityInfo
    {
        /// <summary>
        /// Gets or sets the unique identifier for this activity.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the name of the activity.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the parent activity ID, if any.
        /// </summary>
        public string? ParentId { get; set; }

        /// <summary>
        /// Gets or sets the start time of the activity.
        /// </summary>
        public DateTime StartTime { get; set; }
    }
} 