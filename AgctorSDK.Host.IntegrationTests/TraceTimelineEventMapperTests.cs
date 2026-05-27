using AgctorSDK.Core.Utils.ActivityTracking;
using AgctorSDK.Core.Utils.ActivityTracking.Logger;
using AgctorSDK.Core.Utils.Observability.Visualization;
using AgctorSDK.Host.Services.Traces;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class TraceTimelineEventMapperTests
{
    [Fact]
    public void Map_ToolSpan_SetsEventKindAndStatus()
    {
        var activity = new ActivityInfo
        {
            Id = "a1",
            Name = "tool.PersonMemoryContext",
            DisplayName = "Tool · PersonMemoryContext · BuildContext",
            Timestamp = DateTimeOffset.UtcNow,
            StartTime = DateTimeOffset.UtcNow,
            EndTime = DateTimeOffset.UtcNow.AddMilliseconds(12),
            HasResult = true,
            Status = ActivityStatus.Ok,
            TimelineDetailJson = """{"kind":"agctor.tool.invoke","toolId":"PersonMemoryContextTool","operation":"BuildContext","success":true}"""
        };

        var dto = TraceTimelineEventMapper.Map(activity, 1, activity.Timestamp, new Dictionary<string, int> { ["a1"] = 1 });

        dto.EventKind.Should().Be("tool");
        dto.Status.Should().Be("ok");
        dto.Label.Should().Contain("PersonMemoryContext");
    }
}
