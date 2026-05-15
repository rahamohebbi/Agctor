using System;
using System.Text.Json;
using AgctorSDK.Core.ProjectMemory.Scenarios;
using FluentAssertions;
using Xunit;

namespace AgctorSDK.Core.Tests.ProjectMemory;

public sealed class ScenarioFlowLlmNodeToolIdsTests
{
    [Fact]
    public void ParseFlowDeclaredToolIds_reads_toolIds_array()
    {
        var el = JsonDocument.Parse("""{"toolIds":["person-memory-context","apply-memory-intents"]}""").RootElement;
        var ids = ScenarioFlowLlmNodeToolIds.ParseFlowDeclaredToolIds(el);
        ids.Should().Contain(new[] { "person-memory-context", "apply-memory-intents" });
    }

    [Fact]
    public void ParseFlowDeclaredToolIds_toolPreset_person_memory_read()
    {
        var el = JsonDocument.Parse("""{"toolPreset":"person-memory-read"}""").RootElement;
        ScenarioFlowLlmNodeToolIds.ParseFlowDeclaredToolIds(el).Should().ContainSingle()
            .Which.Should().Be(ScenarioFlowLlmNodeToolIds.PersonMemoryContext);
    }

    [Fact]
    public void UnionAllows_true_when_yaml_or_flow_lists_match()
    {
        ScenarioFlowLlmNodeToolIds.UnionAllows(
                new[] { "file-system" },
                new[] { "person-memory-context" },
                ScenarioFlowLlmNodeToolIds.PersonMemoryContext)
            .Should().BeTrue();
        ScenarioFlowLlmNodeToolIds.UnionAllows(
                new[] { "person-memory-context" },
                Array.Empty<string>(),
                ScenarioFlowLlmNodeToolIds.PersonMemoryContext)
            .Should().BeTrue();
        ScenarioFlowLlmNodeToolIds.UnionAllows(
                new[] { "file-system" },
                Array.Empty<string>(),
                ScenarioFlowLlmNodeToolIds.PersonMemoryContext)
            .Should().BeFalse();
    }
}
