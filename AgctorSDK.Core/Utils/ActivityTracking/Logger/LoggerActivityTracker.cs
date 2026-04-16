using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ActivityInfo>> _traceActivities = new();
        
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
            var traceId = ResolveTraceId(context);
            var activityInfo = new ActivityInfo
            {
                Id = activityId,
                Name = name,
                DisplayName = name,
                TraceId = traceId,
                ParentId = parentId ?? (ActivityStack.Count > 0 ? ActivityStack.Peek().Id : null),
                StartTime = DateTimeOffset.UtcNow,
                Timestamp = DateTimeOffset.UtcNow,
                Status = ActivityStatus.InProgress
            };

            ActivityStack.Push(activityInfo);
            var traceMap = _traceActivities.GetOrAdd(traceId, _ => new ConcurrentDictionary<string, ActivityInfo>());
            traceMap[activityId] = activityInfo;
            
            _logger.Info($"Activity started: {name} [Trace: {traceId}, ID: {activityId}, Parent: {activityInfo.ParentId ?? "none"}]");
            
            return new LoggerActivityScope(this, _logger, activityInfo);
        }

        /// <inheritdoc/>
        public void PropagateContext(IDictionary<string, string> headers)
        {
            if (ActivityStack.Count > 0)
            {
                headers["activity-id"] = ActivityStack.Peek().Id;
                headers["trace-id"] = ActivityStack.Peek().TraceId;
            }
        }

        /// <inheritdoc/>
        public IDictionary<string, string> ExtractContext()
        {
            var context = new Dictionary<string, string>();
            if (ActivityStack.Count > 0)
            {
                context["activity-id"] = ActivityStack.Peek().Id;
                context["trace-id"] = ActivityStack.Peek().TraceId;
            }
            return context;
        }
        
        /// <inheritdoc/>
        public Task<IEnumerable<IActivity>> GetTraceActivitiesAsync(string traceId)
        {
            if (string.IsNullOrWhiteSpace(traceId))
            {
                return Task.FromResult<IEnumerable<IActivity>>(Array.Empty<IActivity>());
            }

            if (!_traceActivities.TryGetValue(traceId, out var activities))
            {
                return Task.FromResult<IEnumerable<IActivity>>(Array.Empty<IActivity>());
            }

            var snapshot = activities.Values
                .OrderBy(a => a.Timestamp)
                .Select(a => a.Clone())
                .Cast<IActivity>()
                .ToList();

            return Task.FromResult<IEnumerable<IActivity>>(snapshot);
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
            }

            activityInfo.EndTime = DateTimeOffset.UtcNow;
            if (activityInfo.Status == ActivityStatus.InProgress)
            {
                activityInfo.Status = ActivityStatus.Completed;
            }

            _logger.Info($"Activity completed: {activityInfo.Name} [Trace: {activityInfo.TraceId}, ID: {activityInfo.Id}] in {activityInfo.Duration.TotalMilliseconds:F2}ms");
        }

        private string ResolveTraceId(IReadOnlyDictionary<string, string>? context)
        {
            if (context != null && context.TryGetValue("trace-id", out var traceId) && !string.IsNullOrWhiteSpace(traceId))
            {
                return traceId;
            }

            if (ActivityStack.Count > 0)
            {
                return ActivityStack.Peek().TraceId;
            }

            return Guid.NewGuid().ToString("N");
        }
    }

    /// <summary>
    /// Represents information about an activity for tracking purposes.
    /// </summary>
    public class ActivityInfo : IActivity
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
        /// Gets or sets the display name of the activity.
        /// </summary>
        public string? DisplayName { get; set; }

        /// <summary>
        /// Gets or sets the parent activity ID, if any.
        /// </summary>
        public string? ParentId { get; set; }

        /// <summary>
        /// Gets or sets the start time of the activity.
        /// </summary>
        public DateTimeOffset StartTime { get; set; }

        /// <summary>
        /// Gets or sets when the activity finished.
        /// </summary>
        public DateTimeOffset? EndTime { get; set; }

        /// <summary>
        /// Gets or sets the trace identifier used to group related activities.
        /// </summary>
        public string TraceId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets whether the activity produced a result.
        /// </summary>
        public bool HasResult { get; set; }

        /// <summary>
        /// Gets or sets the timestamp used for ordering timeline events.
        /// </summary>
        public DateTimeOffset Timestamp { get; set; }

        /// <summary>
        /// Gets or sets the final status for the activity.
        /// </summary>
        public ActivityStatus Status { get; set; } = ActivityStatus.InProgress;

        /// <inheritdoc />
        public string? TimelineDetailJson { get; set; }

        /// <summary>
        /// Gets the measured duration of the activity.
        /// </summary>
        public TimeSpan Duration => (EndTime ?? DateTimeOffset.UtcNow) - StartTime;

        /// <summary>
        /// Creates an immutable snapshot for visualization responses.
        /// </summary>
        public ActivityInfo Clone()
        {
            return new ActivityInfo
            {
                Id = Id,
                Name = Name,
                DisplayName = DisplayName,
                ParentId = ParentId,
                StartTime = StartTime,
                EndTime = EndTime,
                TraceId = TraceId,
                HasResult = HasResult,
                Timestamp = Timestamp,
                Status = Status,
                TimelineDetailJson = TimelineDetailJson
            };
        }
    }
} 