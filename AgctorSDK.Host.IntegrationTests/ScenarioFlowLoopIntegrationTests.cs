using AgctorSDK.Core.ProjectMemory.Scenarios;
using AgctorSDK.Host.Models;
using AgctorSDK.Host.Services.Scenarios;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class ScenarioFlowLoopIntegrationTests : IClassFixture<AgctorWebApplicationFactory>
{
    private readonly AgctorWebApplicationFactory _factory;

    public ScenarioFlowLoopIntegrationTests(AgctorWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public void RequiresRuntimeActor_detects_v2_nodes()
    {
        var flow = new ScenarioFlowDocument
        {
            SchemaVersion = "2.0",
            GraphId = "test",
            Nodes =
            [
                new ScenarioFlowNode { Id = "in", Type = "ChatInput", Label = "In" },
                new ScenarioFlowNode { Id = "ask", Type = "WaitForInput", Label = "Ask" },
                new ScenarioFlowNode { Id = "out", Type = "Output", Label = "Out" }
            ],
            Edges =
            [
                new ScenarioFlowEdge { Id = "e1", FromNodeId = "in", ToNodeId = "ask", Mode = "sequential" },
                new ScenarioFlowEdge { Id = "e2", FromNodeId = "ask", ToNodeId = "out", Mode = "sequential" }
            ]
        };

        ScenarioFlowCapabilities.RequiresRuntimeActor(
            flow.SchemaVersion,
            flow.Nodes.Select(n => n.Type),
            flow.Edges.Select(e => e.Mode)).Should().BeTrue();
    }

    [Fact]
    public void ResolveResumeTargetNode_follows_loopBack_edge()
    {
        var flow = new ScenarioFlowDocument
        {
            GraphId = "loop",
            Nodes =
            [
                new ScenarioFlowNode { Id = "ask", Type = "WaitForInput", Label = "Ask" },
                new ScenarioFlowNode { Id = "ingest", Type = "LlmNode", Label = "Ingest" }
            ],
            Edges =
            [
                new ScenarioFlowEdge
                {
                    Id = "loop",
                    FromNodeId = "ask",
                    ToNodeId = "ingest",
                    Mode = "loopBack",
                    LoopConfig = new ScenarioFlowLoopEdgeConfig
                    {
                        LoopRegionId = "photo-collection",
                        MaxAttempts = 3,
                        StoreInvalidation = "fromTargetForward"
                    }
                }
            ]
        };

        ScenarioFlowGraphInterpreter.ResolveResumeTargetNode(flow, "ask").Should().Be("ingest");
    }
}
