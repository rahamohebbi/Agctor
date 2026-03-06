using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using AgctorSDK.Core.Utils.ActivityTracking;
using AgctorSDK.Core.Utils.ActivityTracking.Logger;
using AgctorSDK.Core.Utils.Logging;
using Xunit;

namespace AgctorSDK.Core.Tests.Utils.ActivityTracking;

public class LoggerActivityTrackerTests
{
    [Fact]
    public async Task GetTraceActivitiesAsync_ReturnsRecordedActivitiesForTrace()
    {
        var tracker = new LoggerActivityTracker(LoggerFactory.CreateLogger("LoggerActivityTrackerTests"));

        string traceId;
        using (tracker.StartActivity("root-operation"))
        {
            var context = tracker.ExtractContext().ToDictionary(pair => pair.Key, pair => pair.Value);
            traceId = context["trace-id"];

            using (var child = tracker.StartActivity("child-operation", context))
            {
                child.SetStatus(ActivityStatus.Ok);
            }
        }

        var activities = (await tracker.GetTraceActivitiesAsync(traceId)).ToList();

        Assert.Equal(2, activities.Count);
        Assert.All(activities, activity => Assert.Equal(traceId, activity.TraceId));
        Assert.Contains(activities, activity => activity.ParentId == null);
        Assert.Contains(activities, activity => activity.ParentId != null);
        Assert.All(activities, activity => Assert.True(activity.Duration.TotalMilliseconds >= 0));
    }
}
