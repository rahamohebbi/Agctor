using AgctorSDK.Core.Tools.Models;
using AgctorSDK.Core.Utils.ActivityTracking;
using FluentAssertions;
using Xunit;

namespace AgctorSDK.Core.Tests.Utils.ActivityTracking;

public sealed class ToolTraceTimelineDetailTests
{
    [Fact]
    public void BuildInvokeJson_IncludesToolOperationAndPreview()
    {
        var json = ToolTraceTimelineDetail.BuildInvokeJson(
            "PersonMemoryContextTool",
            "BuildContext",
            new Dictionary<string, object> { ["projectRoot"] = "C:\\proj", ["scenarioId"] = "people" },
            new ToolResult { IsSuccess = true, Output = "markdown appendix" },
            invokingAgentId: "person-query");

        json.Should().Contain("\"kind\":\"agctor.tool.invoke\"");
        json.Should().Contain("PersonMemoryContextTool");
        json.Should().Contain("BuildContext");
        json.Should().Contain("person-query");
        json.Should().Contain("markdown appendix");
    }

    [Fact]
    public void FormatDisplayName_UsesFriendlyToolLabel()
    {
        ToolTraceTimelineDetail.FormatDisplayName("PersonMemoryContextTool", "BuildContext")
            .Should()
            .Be("Tool · PersonMemoryContext · BuildContext");
    }
}
