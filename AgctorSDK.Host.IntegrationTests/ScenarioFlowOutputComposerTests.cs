using System.Text.Json;
using AgctorSDK.Host.Services.Scenarios;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class ScenarioFlowOutputComposerTests
{
    [Fact]
    public void Compose_Ranked_PrefersPersonQueryOverMemoryCurator()
    {
        var flow = BuildParallelPeopleFlow("ranked");
        var map = flow.Nodes!.ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);
        var store = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["n_query"] = "Ryan can hold a ping pong racket.",
            ["n_curator"] = "Saved skill play ping pong for ryan."
        };

        var text = ScenarioFlowOutputComposer.Compose(flow, map, store, "n_merge", flow.OutputPolicy);

        text.Should().Be("Ryan can hold a ping pong racket.");
    }

    [Fact]
    public void Compose_MergeSections_IncludesLabeledSections()
    {
        var flow = BuildParallelPeopleFlow("merge_sections");
        var map = flow.Nodes!.ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);
        var store = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["n_query"] = "Answer text",
            ["n_curator"] = "Ingest note"
        };

        var text = ScenarioFlowOutputComposer.Compose(flow, map, store, "n_merge", flow.OutputPolicy);

        text.Should().Contain("**");
        text.Should().Contain("Answer text");
        text.Should().Contain("Ingest note");
    }

    [Fact]
    public void PickTranscriptPersonaId_PrefersPersonQuery()
    {
        var picked = ScenarioFlowOutputComposer.PickTranscriptPersonaId(
            new[] { "memory-curator", "person-query", "person-extractor" });

        picked.Should().Be("person-query");
    }

    private static ScenarioFlowDocument BuildParallelPeopleFlow(string outputPolicy) =>
        new()
        {
            SchemaVersion = "1.0",
            GraphId = "composer-test",
            OutputPolicy = outputPolicy,
            Nodes =
            [
                new ScenarioFlowNode
                {
                    Id = "n_query",
                    Type = "LlmNode",
                    Label = "Person query",
                    Config = JsonSerializer.SerializeToElement(new { personaId = "person-query" })
                },
                new ScenarioFlowNode
                {
                    Id = "n_curator",
                    Type = "LlmNode",
                    Label = "Memory curator",
                    Config = JsonSerializer.SerializeToElement(new { personaId = "memory-curator" })
                },
                new ScenarioFlowNode { Id = "n_merge", Type = "Merge", Label = "Merge" }
            ],
            Edges =
            [
                new ScenarioFlowEdge { Id = "e1", FromNodeId = "n_query", ToNodeId = "n_merge", Mode = "parallel" },
                new ScenarioFlowEdge { Id = "e2", FromNodeId = "n_curator", ToNodeId = "n_merge", Mode = "parallel" }
            ]
        };
}
