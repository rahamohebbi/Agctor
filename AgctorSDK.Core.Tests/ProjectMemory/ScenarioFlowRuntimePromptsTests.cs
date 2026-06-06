using AgctorSDK.Core.ProjectMemory.Scenarios;
using FluentAssertions;
using Xunit;

namespace AgctorSDK.Core.Tests.ProjectMemory;

public sealed class ScenarioFlowRuntimePromptsTests
{
    [Fact]
    public void ResolveOriginalUserMessage_finds_ChatInput_from_flow_json()
    {
        var snapshot = new ScenarioFlowRuntimeSnapshot
        {
            Store =
            {
                NodeOutputs =
                {
                    ["in1"] = new ScenarioFlowNodeOutputState { Text = "wedding style advice please" }
                }
            }
        };

        const string flowJson = """
            {"nodes":[
              {"id":"in1","type":"ChatInput"},
              {"id":"n_await","type":"AwaitEvent"}
            ]}
            """;

        ScenarioFlowRuntimePrompts.ResolveOriginalUserMessage(snapshot, flowJson)
            .Should().Be("wedding style advice please");
    }

    [Fact]
    public void BuildPostExtractStyleUserMessage_includes_original_and_photo_hint()
    {
        var snapshot = new ScenarioFlowRuntimeSnapshot
        {
            Store =
            {
                NodeOutputs =
                {
                    ["in1"] = new ScenarioFlowNodeOutputState { Text = "wedding style advice please" }
                }
            }
        };

        var text = ScenarioFlowRuntimePrompts.BuildPostExtractStyleUserMessage(snapshot, null);
        text.Should().Contain("wedding style advice please");
        text.Should().Contain("ONE unified");
        text.Should().Contain("Do not write separate advice per photo");
    }
}
