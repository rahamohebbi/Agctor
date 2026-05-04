using System.Text.Json;
using AgctorSDK.Host.Services.ProjectMemory;
using AgctorSDK.Host.Services.Scenarios;

namespace AgctorSDK.Host.IntegrationTests;

/// <summary>Unit-style tests for playground pipeline chips (Host assembly).</summary>
public sealed class PlaygroundFlowPlanBuilderTests
{
    private static JsonElement JsonPersona(string id) =>
        JsonSerializer.SerializeToElement(new { personaId = id }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    [Fact]
    public void Build_people_like_flow_curator_shows_two_persona_chips_and_ingest_before_curator()
    {
        var scenario = new ScenarioDefinition
        {
            Id = "people",
            Flow = new ScenarioFlowDocument
            {
                Nodes =
                [
                    new ScenarioFlowNode { Id = "in1", Type = "ChatInput", Label = "Chat input", Config = JsonSerializer.SerializeToElement(new { }) },
                    new ScenarioFlowNode { Id = "r1", Type = "Router", Label = "Router", Config = JsonSerializer.SerializeToElement(new { }) },
                    new ScenarioFlowNode { Id = "ext", Type = "LlmNode", Label = "LlmNode", Config = JsonPersona("person-extractor") },
                    new ScenarioFlowNode { Id = "cur", Type = "LlmNode", Label = "LlmNode", Config = JsonPersona("memory-curator") },
                    new ScenarioFlowNode { Id = "out1", Type = "Output", Label = "Output", Config = JsonSerializer.SerializeToElement(new { }) }
                ],
                Edges =
                [
                    new ScenarioFlowEdge { Id = "e1", FromNodeId = "in1", ToNodeId = "r1", Mode = "sequential" },
                    new ScenarioFlowEdge { Id = "e2", FromNodeId = "r1", ToNodeId = "ext", Mode = "sequential" },
                    new ScenarioFlowEdge { Id = "e3", FromNodeId = "ext", ToNodeId = "cur", Mode = "sequential" },
                    new ScenarioFlowEdge { Id = "e4", FromNodeId = "cur", ToNodeId = "out1", Mode = "sequential" }
                ]
            }
        };

        var r = PlaygroundFlowPlanBuilder.Build(scenario, "memory-curator", ingestActive: false);
        r.FromScenarioGraph.Should().BeTrue();
        r.UsedSyntheticLlmNode.Should().BeFalse();

        var labels = r.Steps.Select(s => s.Label).ToList();
        labels.Should().ContainInOrder(
            "Chat input",
            "Router",
            "LlmNode (person-extractor)",
            "Apply extractor JSON → disk",
            "LlmNode (memory-curator)",
            "Output");

        var run = PlaygroundFlowPlanBuilder.ResolveRunnerStepIndex(r.Steps, "memory-curator");
        run.Should().Be(r.Steps.ToList().FindIndex(s => s.Id == "cur"));

        var ingest = r.Steps.First(s => string.Equals(s.NodeKind, "Ingest", StringComparison.OrdinalIgnoreCase));
        ingest.Id.Should().StartWith(PlaygroundFlowPlanBuilder.IngestStepId + "-");
        ingest.Active.Should().BeFalse();
    }

    [Fact]
    public void BuildFlowExecutionPlanPrefix_stops_at_router_with_branching_edges()
    {
        var scenario = new ScenarioDefinition
        {
            Id = "people",
            Flow = new ScenarioFlowDocument
            {
                Nodes =
                [
                    new ScenarioFlowNode { Id = "in1", Type = "ChatInput", Label = "Chat input", Config = JsonSerializer.SerializeToElement(new { }) },
                    new ScenarioFlowNode { Id = "r1", Type = "Router", Label = "Router", Config = JsonSerializer.SerializeToElement(new { }) },
                    new ScenarioFlowNode { Id = "ext", Type = "LlmNode", Label = "LlmNode", Config = JsonPersona("person-extractor") },
                    new ScenarioFlowNode { Id = "cur", Type = "LlmNode", Label = "LlmNode", Config = JsonPersona("memory-curator") },
                    new ScenarioFlowNode { Id = "out1", Type = "Output", Label = "Output", Config = JsonSerializer.SerializeToElement(new { }) }
                ],
                Edges =
                [
                    new ScenarioFlowEdge { Id = "e1", FromNodeId = "in1", ToNodeId = "r1", Mode = "sequential" },
                    new ScenarioFlowEdge { Id = "e2", FromNodeId = "r1", ToNodeId = "ext", Mode = "sequential" },
                    new ScenarioFlowEdge { Id = "e3", FromNodeId = "ext", ToNodeId = "cur", Mode = "sequential" },
                    new ScenarioFlowEdge { Id = "e4", FromNodeId = "cur", ToNodeId = "out1", Mode = "sequential" }
                ]
            }
        };

        var prefix = PlaygroundFlowPlanBuilder.BuildFlowExecutionPlanPrefix(scenario.Flow!, ingestChipActive: true);
        prefix.Should().HaveCount(2);
        prefix[0].NodeKind.Should().Be("ChatInput");
        prefix[1].NodeKind.Should().Be("Router");

        var tail = PlaygroundFlowPlanBuilder.BuildFlowExecutionPlanLinearTail(scenario.Flow!, "ext", ingestChipActive: true);
        tail.Should().NotBeEmpty();
        tail[0].PersonaId.Should().Be("person-extractor");
        tail.Any(s => s.NodeKind == "Ingest").Should().BeTrue();
        tail[^1].NodeKind.Should().Be("Output");
    }

    [Fact]
    public void Build_null_scenario_uses_legacy_ids()
    {
        var r = PlaygroundFlowPlanBuilder.Build(null, "memory-curator", ingestActive: false);
        r.FromScenarioGraph.Should().BeFalse();
        r.Steps.Select(s => s.Id).Should().Equal("chatInput", "router", "llmNode", "ingest", "output");
    }

    [Fact]
    public void Build_agent_not_on_path_inserts_synthetic_persona_before_output()
    {
        var scenario = new ScenarioDefinition
        {
            Id = "x",
            Flow = new ScenarioFlowDocument
            {
                Nodes =
                [
                    new ScenarioFlowNode { Id = "in1", Type = "ChatInput", Label = "In", Config = JsonSerializer.SerializeToElement(new { }) },
                    new ScenarioFlowNode { Id = "p1", Type = "LlmNode", Label = "P", Config = JsonPersona("alice") },
                    new ScenarioFlowNode { Id = "out1", Type = "Output", Label = "Out", Config = JsonSerializer.SerializeToElement(new { }) }
                ],
                Edges =
                [
                    new ScenarioFlowEdge { Id = "e1", FromNodeId = "in1", ToNodeId = "p1", Mode = "sequential" },
                    new ScenarioFlowEdge { Id = "e2", FromNodeId = "p1", ToNodeId = "out1", Mode = "sequential" }
                ]
            }
        };

        var r = PlaygroundFlowPlanBuilder.Build(scenario, "bob", ingestActive: false);
        r.UsedSyntheticLlmNode.Should().BeTrue();
        r.Steps[^2].Label.Should().Be("LlmNode (bob)");
        r.Steps[^2].Id.Should().StartWith(PlaygroundFlowPlanBuilder.SyntheticLlmNodeStepIdPrefix);
        PlaygroundFlowPlanBuilder.ResolveRunnerStepIndex(r.Steps, "bob").Should().Be(r.Steps.Count - 2);
    }
}
