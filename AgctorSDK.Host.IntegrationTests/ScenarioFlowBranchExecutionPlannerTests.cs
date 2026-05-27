using AgctorSDK.Host.Services.Scenarios;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class ScenarioFlowBranchExecutionPlannerTests
{
    [Fact]
    public void InferAuto_WritePlusQuery_IsSequential()
    {
        ScenarioFlowBranchExecutionPlanner
            .InferAuto(new[] { "person-extractor", "person-query" })
            .Should()
            .Be(ScenarioFlowRouterBranchExecution.Sequential);
    }

    [Fact]
    public void InferAuto_TwoCoaches_IsParallel()
    {
        ScenarioFlowBranchExecutionPlanner
            .InferAuto(new[] { "style-coach", "fitness-coach" })
            .Should()
            .Be(ScenarioFlowRouterBranchExecution.Parallel);
    }

    [Fact]
    public void OrderBranchStarts_PutsExtractorBeforeQuery()
    {
        var flow = new ScenarioFlowDocument
        {
            Nodes =
            [
                new ScenarioFlowNode { Id = "q", Type = "LlmNode", Config = JsonPersona("person-query") },
                new ScenarioFlowNode { Id = "x", Type = "LlmNode", Config = JsonPersona("person-extractor") }
            ]
        };
        var map = flow.Nodes!.ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);
        var ordered = ScenarioFlowBranchExecutionPlanner.OrderBranchStarts(flow, map, new[] { "q", "x" });
        ordered.Should().Equal("x", "q");
    }

    [Fact]
    public void Resolve_AutoConfig_UsesLlmChoice()
    {
        var cfg = new ScenarioFlowRouterConfig(
            ScenarioFlowRouterMode.Llm,
            null,
            null,
            null,
            null,
            ScenarioFlowRouterTargetPolicy.AllMatching,
            ScenarioFlowRouterBranchExecution.Auto);
        ScenarioFlowBranchExecutionPlanner
            .Resolve(cfg, ScenarioFlowRouterBranchExecution.Parallel, new[] { "a", "b" })
            .Should()
            .Be(ScenarioFlowRouterBranchExecution.Parallel);
    }

    private static System.Text.Json.JsonElement? JsonPersona(string id)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new { personaId = id });
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
