using AgctorSDK.Core.ProjectMemory.Scenarios;
using FluentAssertions;
using Xunit;

namespace AgctorSDK.Core.Tests.ProjectMemory;

public sealed class ScenarioFlowGraphNavigationTests
{
    private const string FlowJson = """
        {"nodes":[],"edges":[
          {"id":"e1","fromNodeId":"n_ask","toNodeId":"n_visual","mode":"loopBack"},
          {"id":"e2","fromNodeId":"n_await","toNodeId":"n_style","mode":"loopBack"}
        ]}
        """;

    [Fact]
    public void ResolveResumeTargetNode_prefers_loopBack_from_wait_for_input()
    {
        ScenarioFlowGraphNavigation.ResolveResumeTargetNode(FlowJson, "n_ask").Should().Be("n_visual");
    }

    [Fact]
    public void ResolveResumeTargetNode_prefers_loopBack_from_await_event()
    {
        ScenarioFlowGraphNavigation.ResolveResumeTargetNode(FlowJson, "n_await").Should().Be("n_style");
    }
}
