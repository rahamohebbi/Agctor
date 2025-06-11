using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using AgctorSDK.Core.Utils.Logging;
using AgctorSDK.Core.Utils.Observability.Visualization;

namespace AgctorSDK.Core.Utils.ActivityTracking
{
    /// <summary>
    /// Implementation of activity tracking using OpenTelemetry.
    /// </summary>
    public class OpenTelemetryActivityTracker : IActivityTracker
    {
        private readonly System.Diagnostics.ActivitySource _activitySource;
        private readonly IAgctorLogger _logger;

        /// <summary>
        /// Initializes a new instance of the OpenTelemetryActivityTracker class.
        /// </summary>
        /// <param name="activitySource">The activity source for creating activities.</param>
        /// <param name="logger">Logger for diagnostic information.</param>
        public OpenTelemetryActivityTracker(
            System.Diagnostics.ActivitySource activitySource, 
            IAgctorLogger logger)
        {
            _activitySource = activitySource ?? throw new ArgumentNullException(nameof(activitySource));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public IActivityScope StartActivity(string name, IReadOnlyDictionary<string, string>? context = null)
        {
            _logger.Debug($"Starting activity: {name}");
            
            try
            {
                System.Diagnostics.ActivityContext? activityContext = null;
                if (context != null && 
                    context.TryGetValue("trace-id", out var traceIdStr) && 
                    context.TryGetValue("span-id", out var spanIdStr))
                {
                    var traceId = ActivityTraceId.CreateFromString(traceIdStr);
                    var spanId = ActivitySpanId.CreateFromString(spanIdStr);
                    activityContext = new System.Diagnostics.ActivityContext(
                        traceId,
                        spanId,
                        ActivityTraceFlags.Recorded);
                }
                
                var activity = _activitySource.StartActivity(
                    name,
                    ActivityKind.Internal,
                    activityContext ?? default);
                
                if (activity == null)
                {
                    _logger.Warning($"Failed to start activity: {name}");
                    return new NullActivityScope(name);
                }
                
                return new OpenTelemetryActivityScope(activity, _logger);
            }
            catch (Exception ex)
            {
                _logger.Error($"Error starting activity {name}: {ex.Message}");
                return new NullActivityScope(name);
            }
        }

        /// <inheritdoc />
        public IDictionary<string, string> ExtractContext()
        {
            var current = System.Diagnostics.Activity.Current;
            if (current == null)
            {
                _logger.Debug("No current activity found for context extraction");
                return new Dictionary<string, string>();
            }
            
            return new Dictionary<string, string>
            {
                ["trace-id"] = current.TraceId.ToString(),
                ["span-id"] = current.SpanId.ToString()
            };
        }
        
        /// <inheritdoc />
        public void PropagateContext(IDictionary<string, string> headers)
        {
            var current = System.Diagnostics.Activity.Current;
            if (current == null)
            {
                _logger.Debug("No current activity found for context propagation");
                return;
            }
            
            headers["trace-id"] = current.TraceId.ToString();
            headers["span-id"] = current.SpanId.ToString();
            
            // Add any additional context required for distributed tracing
            if (current.TraceStateString != null)
            {
                headers["tracestate"] = current.TraceStateString;
            }
        }
        
        /// <inheritdoc />
        public Task<IEnumerable<IActivity>> GetTraceActivitiesAsync(string traceId)
        {
            _logger.Debug($"Getting activities for trace: {traceId}");
            
            // In a real implementation, this would query stored trace data
            // For demonstration purposes, we'll return an empty collection
            _logger.Warning("GetTraceActivitiesAsync is not fully implemented - this is a placeholder");
            
            // Return a placeholder result
            var placeholderActivities = new List<IActivity>
            {
                new PlaceholderActivity
                {
                    Id = "span1",
                    ParentId = null,
                    Name = "RootOperation",
                    DisplayName = "Root Operation",
                    Duration = TimeSpan.FromMilliseconds(500),
                    HasResult = true,
                    TraceId = traceId,
                    Timestamp = DateTimeOffset.UtcNow.AddSeconds(-10)
                },
                new PlaceholderActivity
                {
                    Id = "span2",
                    ParentId = "span1",
                    Name = "ChildOperation1",
                    DisplayName = "Child Operation 1",
                    Duration = TimeSpan.FromMilliseconds(200),
                    HasResult = true,
                    TraceId = traceId,
                    Timestamp = DateTimeOffset.UtcNow.AddSeconds(-9)
                },
                new PlaceholderActivity
                {
                    Id = "span3",
                    ParentId = "span1",
                    Name = "ChildOperation2",
                    DisplayName = "Child Operation 2",
                    Duration = TimeSpan.FromMilliseconds(150),
                    HasResult = true,
                    TraceId = traceId,
                    Timestamp = DateTimeOffset.UtcNow.AddSeconds(-8)
                }
            };
            
            return Task.FromResult<IEnumerable<IActivity>>(placeholderActivities);
        }
    }

    /// <summary>
    /// OpenTelemetry implementation of the activity scope interface.
    /// </summary>
    internal class OpenTelemetryActivityScope : IActivityScope, IActivity
    {
        private readonly System.Diagnostics.Activity _activity;
        private readonly IAgctorLogger _logger;
        private bool _hasResult;
        private bool _disposed = false;

        /// <summary>
        /// Initializes a new instance of the OpenTelemetryActivityScope class.
        /// </summary>
        /// <param name="activity">The underlying OpenTelemetry activity.</param>
        /// <param name="logger">Logger for diagnostic information.</param>
        public OpenTelemetryActivityScope(System.Diagnostics.Activity activity, IAgctorLogger logger)
        {
            _activity = activity ?? throw new ArgumentNullException(nameof(activity));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public string Id => _activity.Id ?? string.Empty;

        /// <inheritdoc />
        public ActivityStatus Status { get; private set; } = ActivityStatus.InProgress;

        /// <inheritdoc />
        public TimeSpan Duration => _activity.Duration;

        /// <inheritdoc />
        public bool HasResult => _hasResult;

        /// <inheritdoc />
        public void AddEvent(string name)
        {
            _activity.AddEvent(new ActivityEvent(name));
        }

        /// <inheritdoc />
        public void SetAttribute(string key, string value)
        {
            _activity.SetTag(key, value);
        }

        /// <inheritdoc />
        public void SetAttribute(string key, int value)
        {
            _activity.SetTag(key, value);
        }

        /// <inheritdoc />
        public void SetAttribute(string key, double value)
        {
            _activity.SetTag(key, value);
        }

        /// <inheritdoc />
        public void SetAttribute(string key, bool value)
        {
            _activity.SetTag(key, value);
        }

        /// <inheritdoc />
        public void SetResult(object result)
        {
            _hasResult = true;
            _activity.SetTag("result.type", result?.GetType().Name ?? "null");
            
            // Attempt to get a string representation of the result
            string resultString;
            try
            {
                resultString = result?.ToString() ?? "null";
                
                // Truncate if too long
                if (resultString.Length > 100)
                {
                    resultString = resultString.Substring(0, 97) + "...";
                }
            }
            catch
            {
                resultString = "[Unable to convert result to string]";
            }
            
            _activity.SetTag("result.value", resultString);
        }

        /// <inheritdoc />
        public void SetError(Exception exception)
        {
            Status = ActivityStatus.Error;
            
            _activity.SetTag("error", true);
            _activity.SetTag("error.type", exception.GetType().Name);
            _activity.SetTag("error.message", exception.Message);
            
            // Add stack trace as an event
            var stackTraceEvent = new ActivityEvent("exception", tags: new ActivityTagsCollection
            {
                { "exception.type", exception.GetType().FullName },
                { "exception.message", exception.Message },
                { "exception.stacktrace", exception.StackTrace }
            });
            
            _activity.AddEvent(stackTraceEvent);
        }

        /// <inheritdoc />
        public void Complete()
        {
            Status = ActivityStatus.Completed;
            _activity.Stop();
        }

        /// <inheritdoc />
        public IDictionary<string, string> ExtractContext()
        {
            return new Dictionary<string, string>
            {
                ["trace-id"] = _activity.TraceId.ToString(),
                ["span-id"] = _activity.SpanId.ToString()
            };
        }
        
        /// <inheritdoc />
        public string? ParentId => _activity.ParentSpanId.ToString();
        
        /// <inheritdoc />
        public string? Name => _activity.OperationName;
        
        /// <inheritdoc />
        public string? DisplayName => _activity.DisplayName;
        
        /// <inheritdoc />
        public string TraceId => _activity.TraceId.ToString();
        
        /// <inheritdoc />
        public DateTimeOffset Timestamp => _activity.StartTimeUtc;

        /// <inheritdoc />
        public void SetStatus(ActivityStatus status, string? description = null)
        {
            Status = status;
            
            if (description != null)
            {
                _activity.SetTag("status.description", description);
            }
            
            switch (status)
            {
                case ActivityStatus.Ok:
                    _activity.SetStatus(ActivityStatusCode.Ok);
                    break;
                case ActivityStatus.Error:
                    _activity.SetStatus(ActivityStatusCode.Error);
                    break;
            }
        }

        /// <inheritdoc />
        public void RecordEvent(string name, IReadOnlyDictionary<string, object>? attributes = null)
        {
            if (attributes == null)
            {
                _activity.AddEvent(new ActivityEvent(name));
                return;
            }
            
            var tags = new ActivityTagsCollection();
            foreach (var kvp in attributes)
            {
                tags.Add(kvp.Key, kvp.Value);
            }
            
            _activity.AddEvent(new ActivityEvent(name, tags: tags));
        }

        /// <inheritdoc />
        public void RecordException(Exception exception)
        {
            SetError(exception);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
                return;
            
            _disposed = true;
            
            // Only complete if not already completed
            if (Status == ActivityStatus.InProgress)
            {
                Complete();
            }
        }
    }

    /// <summary>
    /// Null implementation of the activity scope interface for when activity creation fails.
    /// </summary>
    internal class NullActivityScope : IActivityScope, IActivity
    {
        private readonly string _name;
        private readonly DateTimeOffset _startTime;
        private bool _disposed = false;

        /// <summary>
        /// Initializes a new instance of the NullActivityScope class.
        /// </summary>
        /// <param name="name">The name of the activity.</param>
        public NullActivityScope(string name)
        {
            _name = name;
            _startTime = DateTimeOffset.UtcNow;
            Id = Guid.NewGuid().ToString();
            TraceId = Guid.NewGuid().ToString();
        }

        /// <inheritdoc />
        public string Id { get; }

        /// <inheritdoc />
        public ActivityStatus Status { get; private set; } = ActivityStatus.InProgress;

        /// <inheritdoc />
        public TimeSpan Duration => DateTimeOffset.UtcNow - _startTime;

        /// <inheritdoc />
        public bool HasResult { get; private set; }

        /// <inheritdoc />
        public void AddEvent(string name)
        {
            // No-op
        }

        /// <inheritdoc />
        public void SetAttribute(string key, string value)
        {
            // No-op
        }

        /// <inheritdoc />
        public void SetAttribute(string key, int value)
        {
            // No-op
        }

        /// <inheritdoc />
        public void SetAttribute(string key, double value)
        {
            // No-op
        }

        /// <inheritdoc />
        public void SetAttribute(string key, bool value)
        {
            // No-op
        }

        /// <inheritdoc />
        public void SetResult(object result)
        {
            HasResult = true;
        }

        /// <inheritdoc />
        public void SetError(Exception exception)
        {
            Status = ActivityStatus.Error;
        }

        /// <inheritdoc />
        public void Complete()
        {
            Status = ActivityStatus.Completed;
        }

        /// <inheritdoc />
        public IDictionary<string, string> ExtractContext()
        {
            return new Dictionary<string, string>();
        }
        
        /// <inheritdoc />
        public string? ParentId => null;
        
        /// <inheritdoc />
        public string? Name => _name;
        
        /// <inheritdoc />
        public string? DisplayName => _name;
        
        /// <inheritdoc />
        public string TraceId { get; }
        
        /// <inheritdoc />
        public DateTimeOffset Timestamp => _startTime;

        /// <inheritdoc />
        public void SetStatus(ActivityStatus status, string? description = null)
        {
            Status = status;
        }

        /// <inheritdoc />
        public void RecordEvent(string name, IReadOnlyDictionary<string, object>? attributes = null)
        {
            // No-op
        }

        /// <inheritdoc />
        public void RecordException(Exception exception)
        {
            Status = ActivityStatus.Error;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
                return;
            
            _disposed = true;
            
            // Only complete if not already completed
            if (Status == ActivityStatus.InProgress)
            {
                Complete();
            }
        }
    }
    
    /// <summary>
    /// Placeholder activity for demo purposes.
    /// </summary>
    internal class PlaceholderActivity : IActivity
    {
        /// <inheritdoc />
        public string Id { get; set; } = string.Empty;
        
        /// <inheritdoc />
        public string? ParentId { get; set; }
        
        /// <inheritdoc />
        public string? Name { get; set; }
        
        /// <inheritdoc />
        public string? DisplayName { get; set; }
        
        /// <inheritdoc />
        public TimeSpan Duration { get; set; }
        
        /// <inheritdoc />
        public bool HasResult { get; set; }
        
        /// <inheritdoc />
        public string TraceId { get; set; } = string.Empty;
        
        /// <inheritdoc />
        public DateTimeOffset Timestamp { get; set; }
        
        /// <inheritdoc />
        public ActivityStatus Status { get; set; } = ActivityStatus.Completed;
        
        /// <inheritdoc />
        public void AddEvent(string name) { }
        
        /// <inheritdoc />
        public void SetAttribute(string key, string value) { }
        
        /// <inheritdoc />
        public void SetAttribute(string key, int value) { }
        
        /// <inheritdoc />
        public void SetAttribute(string key, double value) { }
        
        /// <inheritdoc />
        public void SetAttribute(string key, bool value) { }
        
        /// <inheritdoc />
        public void SetResult(object result) { }
        
        /// <inheritdoc />
        public void SetError(Exception exception) { }
        
        /// <inheritdoc />
        public void Complete() { }
        
        /// <inheritdoc />
        public IDictionary<string, string> ExtractContext()
        {
            return new Dictionary<string, string>();
        }
    }
} 