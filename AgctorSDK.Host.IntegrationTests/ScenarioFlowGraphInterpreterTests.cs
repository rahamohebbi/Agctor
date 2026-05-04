using System.Text.Json;
using AgctorSDK.Host.Services.Scenarios;

namespace AgctorSDK.Host.IntegrationTests;

/// <summary>PRD-014: in-process tests for <see cref="ScenarioFlowGraphInterpreter"/> (no Ollama).</summary>
public sealed class ScenarioFlowGraphInterpreterTests
{
    [Fact]
    public async Task ExecuteAsync_LinearLlmNode_ReturnsOutput()
    {
        var flow = new ScenarioFlowDocument
        {
            SchemaVersion = "1.0",
            GraphId = "t1",
            OutputPolicy = "merge_sections",
            Nodes =
            [
                new ScenarioFlowNode { Id = "in1", Type = "ChatInput", Label = "In" },
                new ScenarioFlowNode { Id = "p1", Type = "LlmNode", Label = "P", Config = JsonPersona("alice") },
                new ScenarioFlowNode { Id = "out1", Type = "Output", Label = "Out" }
            ],
            Edges =
            [
                new ScenarioFlowEdge { Id = "e1", FromNodeId = "in1", ToNodeId = "p1", Mode = "sequential" },
                new ScenarioFlowEdge { Id = "e2", FromNodeId = "p1", ToNodeId = "out1", Mode = "sequential" }
            ]
        };

        var interpreter = new ScenarioFlowGraphInterpreter();
        var log = new List<(string id, string prompt)>();
        var result = await interpreter.ExecuteAsync(
            flow,
            "hello",
            async (personaId, prompt, _, _) =>
            {
                log.Add((personaId, prompt));
                return await Task.FromResult($"[{personaId}:{prompt}]");
            },
            CancellationToken.None);

        result.Should().Be("[alice:hello]");
        log.Should().ContainSingle();
        log[0].id.Should().Be("alice");
        log[0].prompt.Should().Be("hello");
    }

    [Fact]
    public async Task ExecuteAsync_Router_PicksBranchBySubstring()
    {
        var flow = new ScenarioFlowDocument
        {
            SchemaVersion = "1.0",
            GraphId = "t2",
            OutputPolicy = "merge_sections",
            Nodes =
            [
                new ScenarioFlowNode { Id = "in1", Type = "ChatInput", Label = "In" },
                new ScenarioFlowNode { Id = "r1", Type = "Router", Label = "R" },
                new ScenarioFlowNode { Id = "pA", Type = "LlmNode", Label = "A", Config = JsonPersona("a") },
                new ScenarioFlowNode { Id = "pB", Type = "LlmNode", Label = "B", Config = JsonPersona("b") },
                new ScenarioFlowNode { Id = "out1", Type = "Output", Label = "Out" }
            ],
            Edges =
            [
                new ScenarioFlowEdge { Id = "e0", FromNodeId = "in1", ToNodeId = "r1", Mode = "sequential" },
                new ScenarioFlowEdge { Id = "e1", FromNodeId = "r1", ToNodeId = "pA", Mode = "sequential", Condition = "USE_A" },
                new ScenarioFlowEdge { Id = "e2", FromNodeId = "r1", ToNodeId = "pB", Mode = "sequential", Condition = "" },
                new ScenarioFlowEdge { Id = "e3", FromNodeId = "pA", ToNodeId = "out1", Mode = "sequential" },
                new ScenarioFlowEdge { Id = "e4", FromNodeId = "pB", ToNodeId = "out1", Mode = "sequential" }
            ]
        };

        var interpreter = new ScenarioFlowGraphInterpreter();
        var chosen = new List<string>();
        var r = await interpreter.ExecuteAsync(
            flow,
            "prefix USE_A suffix",
            async (personaId, prompt, _, _) =>
            {
                chosen.Add(personaId);
                return await Task.FromResult(personaId);
            },
            CancellationToken.None);

        r.Should().Be("a");
        chosen.Should().Equal("a");
    }

    [Fact]
    public async Task ExecuteAsync_ParallelFanOut_MergesAtSharedMerge()
    {
        var flow = new ScenarioFlowDocument
        {
            SchemaVersion = "1.0",
            GraphId = "p1",
            OutputPolicy = "merge_sections",
            Nodes =
            [
                new ScenarioFlowNode { Id = "in1", Type = "ChatInput", Label = "In" },
                new ScenarioFlowNode { Id = "p1", Type = "LlmNode", Label = "P1", Config = JsonPersona("a") },
                new ScenarioFlowNode { Id = "p2", Type = "LlmNode", Label = "P2", Config = JsonPersona("b") },
                new ScenarioFlowNode { Id = "m1", Type = "Merge", Label = "M" },
                new ScenarioFlowNode { Id = "out1", Type = "Output", Label = "Out" }
            ],
            Edges =
            [
                new ScenarioFlowEdge { Id = "ep1", FromNodeId = "in1", ToNodeId = "p1", Mode = "parallel" },
                new ScenarioFlowEdge { Id = "ep2", FromNodeId = "in1", ToNodeId = "p2", Mode = "parallel" },
                new ScenarioFlowEdge { Id = "es1", FromNodeId = "p1", ToNodeId = "m1", Mode = "sequential" },
                new ScenarioFlowEdge { Id = "es2", FromNodeId = "p2", ToNodeId = "m1", Mode = "sequential" },
                new ScenarioFlowEdge { Id = "es3", FromNodeId = "m1", ToNodeId = "out1", Mode = "sequential" }
            ]
        };

        var interpreter = new ScenarioFlowGraphInterpreter();
        var r = await interpreter.ExecuteAsync(
            flow,
            "hi",
            async (personaId, prompt, _, _) => await Task.FromResult($"{personaId}:{prompt}"),
            CancellationToken.None);

        r.Should().Contain("a:hi");
        r.Should().Contain("b:hi");
    }

    [Fact]
    public async Task ExecuteAsync_NestedParallel_Throws()
    {
        var flow = new ScenarioFlowDocument
        {
            SchemaVersion = "1.0",
            GraphId = "nest",
            OutputPolicy = "merge_sections",
            Nodes =
            [
                new ScenarioFlowNode { Id = "in1", Type = "ChatInput", Label = "In" },
                new ScenarioFlowNode { Id = "p1", Type = "LlmNode", Label = "P1", Config = JsonPersona("p1") },
                new ScenarioFlowNode { Id = "p2", Type = "LlmNode", Label = "P2", Config = JsonPersona("p2") },
                new ScenarioFlowNode { Id = "x1", Type = "LlmNode", Label = "X", Config = JsonPersona("x") },
                new ScenarioFlowNode { Id = "x2", Type = "LlmNode", Label = "Y", Config = JsonPersona("y") },
                new ScenarioFlowNode { Id = "m1", Type = "Merge", Label = "M" },
                new ScenarioFlowNode { Id = "out1", Type = "Output", Label = "Out" }
            ],
            Edges =
            [
                new ScenarioFlowEdge { Id = "e0", FromNodeId = "in1", ToNodeId = "p1", Mode = "parallel" },
                new ScenarioFlowEdge { Id = "e0b", FromNodeId = "in1", ToNodeId = "p2", Mode = "parallel" },
                new ScenarioFlowEdge { Id = "eNest1", FromNodeId = "p1", ToNodeId = "x1", Mode = "parallel" },
                new ScenarioFlowEdge { Id = "eNest2", FromNodeId = "p1", ToNodeId = "x2", Mode = "parallel" },
                new ScenarioFlowEdge { Id = "e1", FromNodeId = "x1", ToNodeId = "m1", Mode = "sequential" },
                new ScenarioFlowEdge { Id = "e2", FromNodeId = "x2", ToNodeId = "m1", Mode = "sequential" },
                new ScenarioFlowEdge { Id = "e2b", FromNodeId = "p2", ToNodeId = "m1", Mode = "sequential" },
                new ScenarioFlowEdge { Id = "e3", FromNodeId = "m1", ToNodeId = "out1", Mode = "sequential" }
            ]
        };

        var interpreter = new ScenarioFlowGraphInterpreter();
        var act = async () => await interpreter.ExecuteAsync(
            flow,
            "hi",
            async (_, prompt, _, _) => await Task.FromResult(prompt),
            CancellationToken.None);

        var ex = await Assert.ThrowsAsync<ScenarioFlowExecutionException>(act);
        ex.Message.ToLowerInvariant().Should().Contain("nested parallel fan-out");
    }

    [Fact]
    public async Task ExecuteAsync_LlmRouter_MultiTarget_ParallelMerge()
    {
        var flow = new ScenarioFlowDocument
        {
            SchemaVersion = "1.0",
            GraphId = "llm-r1",
            OutputPolicy = "merge_sections",
            Nodes =
            [
                new ScenarioFlowNode { Id = "in1", Type = "ChatInput", Label = "In" },
                new ScenarioFlowNode { Id = "r1", Type = "Router", Label = "R", Config = JsonRouterLlm() },
                new ScenarioFlowNode { Id = "pA", Type = "LlmNode", Label = "A", Config = JsonPersona("a") },
                new ScenarioFlowNode { Id = "pB", Type = "LlmNode", Label = "B", Config = JsonPersona("b") },
                new ScenarioFlowNode { Id = "m1", Type = "Merge", Label = "M" },
                new ScenarioFlowNode { Id = "out1", Type = "Output", Label = "Out" }
            ],
            Edges =
            [
                new ScenarioFlowEdge { Id = "e0", FromNodeId = "in1", ToNodeId = "r1", Mode = "sequential" },
                new ScenarioFlowEdge { Id = "e1", FromNodeId = "r1", ToNodeId = "pA", Mode = "sequential" },
                new ScenarioFlowEdge { Id = "e2", FromNodeId = "r1", ToNodeId = "pB", Mode = "sequential" },
                new ScenarioFlowEdge { Id = "e3", FromNodeId = "pA", ToNodeId = "m1", Mode = "sequential" },
                new ScenarioFlowEdge { Id = "e4", FromNodeId = "pB", ToNodeId = "m1", Mode = "sequential" },
                new ScenarioFlowEdge { Id = "e5", FromNodeId = "m1", ToNodeId = "out1", Mode = "sequential" }
            ]
        };

        var fake = new FakeScenarioFlowRouterLlmService
        {
            Next = ScenarioFlowRouterLlmResult.Success(new[] { "a", "b" })
        };

        var interpreter = new ScenarioFlowGraphInterpreter();
        var r = await interpreter.ExecuteAsync(
            flow,
            "hi",
            async (personaId, prompt, _, _) => await Task.FromResult($"{personaId}:{prompt}"),
            Timeout.InfiniteTimeSpan,
            "/tmp",
            fake,
            observer: null,
            CancellationToken.None);

        r.Should().Contain("a:hi");
        r.Should().Contain("b:hi");
    }

    [Fact]
    public async Task ExecuteAsync_LlmRouter_Clarification_ReturnsPrompt()
    {
        var flow = new ScenarioFlowDocument
        {
            SchemaVersion = "1.0",
            GraphId = "llm-r2",
            OutputPolicy = "merge_sections",
            Nodes =
            [
                new ScenarioFlowNode { Id = "in1", Type = "ChatInput", Label = "In" },
                new ScenarioFlowNode { Id = "r1", Type = "Router", Label = "R", Config = JsonRouterLlm() },
                new ScenarioFlowNode { Id = "pA", Type = "LlmNode", Label = "A", Config = JsonPersona("a") },
                new ScenarioFlowNode { Id = "out1", Type = "Output", Label = "Out" }
            ],
            Edges =
            [
                new ScenarioFlowEdge { Id = "e0", FromNodeId = "in1", ToNodeId = "r1", Mode = "sequential" },
                new ScenarioFlowEdge { Id = "e1", FromNodeId = "r1", ToNodeId = "pA", Mode = "sequential" },
                new ScenarioFlowEdge { Id = "e2", FromNodeId = "pA", ToNodeId = "out1", Mode = "sequential" }
            ]
        };

        var fake = new FakeScenarioFlowRouterLlmService { Next = ScenarioFlowRouterLlmResult.Clarify("Say more?") };
        var interpreter = new ScenarioFlowGraphInterpreter();
        var r = await interpreter.ExecuteAsync(
            flow,
            "x",
            async (_, _, _, _) => await Task.FromResult("no"),
            Timeout.InfiniteTimeSpan,
            "/tmp",
            fake,
            observer: null,
            CancellationToken.None);

        r.Should().Be("Say more?");
    }

    [Fact]
    public async Task ExecuteAsync_LlmRouter_RoutingContext_IsUpstreamPersonaOutput()
    {
        var flow = new ScenarioFlowDocument
        {
            SchemaVersion = "1.0",
            GraphId = "ctx1",
            OutputPolicy = "merge_sections",
            Nodes =
            [
                new ScenarioFlowNode { Id = "in1", Type = "ChatInput", Label = "In" },
                new ScenarioFlowNode { Id = "p1", Type = "LlmNode", Label = "Pre", Config = JsonPersona("alice") },
                new ScenarioFlowNode { Id = "r1", Type = "Router", Label = "R", Config = JsonRouterLlm() },
                new ScenarioFlowNode { Id = "pA", Type = "LlmNode", Label = "A", Config = JsonPersona("a") },
                new ScenarioFlowNode { Id = "out1", Type = "Output", Label = "Out" }
            ],
            Edges =
            [
                new ScenarioFlowEdge { Id = "e0", FromNodeId = "in1", ToNodeId = "p1", Mode = "sequential" },
                new ScenarioFlowEdge { Id = "e1", FromNodeId = "p1", ToNodeId = "r1", Mode = "sequential" },
                new ScenarioFlowEdge { Id = "e2", FromNodeId = "r1", ToNodeId = "pA", Mode = "sequential" },
                new ScenarioFlowEdge { Id = "e3", FromNodeId = "pA", ToNodeId = "out1", Mode = "sequential" }
            ]
        };

        var fake = new FakeScenarioFlowRouterLlmService { Next = ScenarioFlowRouterLlmResult.Success(new[] { "a" }) };
        var interpreter = new ScenarioFlowGraphInterpreter();
        var r = await interpreter.ExecuteAsync(
            flow,
            "hello",
            (personaId, prompt, _, _) =>
            {
                if (string.Equals(personaId, "alice", StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult("TRANSFORMED:" + prompt);
                return Task.FromResult($"{personaId}:{prompt}");
            },
            Timeout.InfiniteTimeSpan,
            "/tmp",
            fake,
            observer: null,
            CancellationToken.None);

        fake.LastRoutingContext.Should().Be("TRANSFORMED:hello");
        r.Should().Be("a:TRANSFORMED:hello");
    }

    [Fact]
    public void HasParallelEdges_DetectsParallel()
    {
        var flow = new ScenarioFlowDocument
        {
            SchemaVersion = "1.0",
            GraphId = "t3",
            Nodes = [new ScenarioFlowNode { Id = "x", Type = "ChatInput", Label = "x" }],
            Edges = [new ScenarioFlowEdge { Id = "e", FromNodeId = "x", ToNodeId = "x", Mode = "parallel" }]
        };
        ScenarioFlowGraphInterpreter.HasParallelEdges(flow).Should().BeTrue();
    }

    private static JsonElement? JsonPersona(string id)
    {
        var json = JsonSerializer.Serialize(new { personaId = id });
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static JsonElement? JsonRouterLlm()
    {
        var json = JsonSerializer.Serialize(new { routerMode = "llm" });
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
