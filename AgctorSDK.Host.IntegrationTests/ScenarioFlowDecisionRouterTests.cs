using System.Text.Json;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Orchestration;
using AgctorSDK.Host.Services;
using AgctorSDK.Host.Services.Scenarios;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class ScenarioFlowDecisionRouterTests
{
    private static readonly ScenarioDecisionBranch[] Branches = [new("writer", "Save facts"), new("reader", "Read saved facts")];
    private static ScenarioFlowRouterConfig Config(bool single = true) => new(
        ScenarioFlowRouterMode.Decision, null, null, null, "Use the branch hints.",
        single ? ScenarioFlowRouterTargetPolicy.SingleBest : ScenarioFlowRouterTargetPolicy.AllMatching,
        DecisionModel: "tev1:4b-q4_K_M");
    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();
    private static string Choice(string choice = "branch_1", double probability = 0.8) =>
        JsonSerializer.Serialize(new { answers = new { route = new { type = "choice", choice,
            probabilities = new Dictionary<string, double> { ["branch_0"] = 1 - probability, ["branch_1"] = probability, ["no_match"] = 0 }, confidence = 0.01 } } });

    [Fact]
    public async Task SingleBest_UsesSelectedModelAndOptionProbability_NotConfidence()
    {
        var client = new StubClient(Choice());
        var result = await new ScenarioFlowDecisionRouter(client).RouteAsync("Read it", Branches, Config() with { MinConfidence = 0.7 }, default);
        result.SelectedPersonaIds.Should().Equal("reader");
        client.Model.Should().Be("tev1:4b-q4_K_M");
        client.State.Should().Be("Read it");
        var question = client.Questions.GetProperty("route");
        question.GetProperty("type").GetString().Should().Be("choice");
        question.GetProperty("criteria").GetProperty("branch_0").GetString().Should().Be("Save facts");
        question.GetProperty("criteria").TryGetProperty("no_match", out _).Should().BeTrue();
        question.GetProperty("instructions").GetString().Should().Contain("Use the branch hints.");
    }

    [Fact]
    public async Task AllMatching_ScoresIndependently_CapsAndResolvesExecution()
    {
        var client = new StubClient("""
            {"answers":{"branch_0":{"type":"noul","noul":0.7},"branch_1":{"type":"noul","noul":0.9},
            "execution":{"type":"choice","choice":"sequential","probabilities":{"sequential":0.9,"parallel":0.1}}}}
            """);
        var config = Config(false) with { BranchExecution = ScenarioFlowRouterBranchExecution.Auto };
        var router = new ScenarioFlowDecisionRouter(client);
        var result = await router.RouteAsync("Save then read", Branches, config, default);
        result.SelectedPersonaIds.Should().Equal("reader", "writer");
        result.ResolvedBranchExecution.Should().Be(ScenarioFlowRouterBranchExecution.Sequential);
        client.Questions.GetProperty("branch_0").GetProperty("type").GetString().Should().Be("noul");
        result = await router.RouteAsync("Save then read", Branches, config with { MaxTargets = 1 }, default);
        result.SelectedPersonaIds.Should().Equal("reader");
    }

    [Fact]
    public async Task FalseOrTiedBoolean_DoesNotRoute_EvenWithZeroThreshold()
    {
        var router = new ScenarioFlowDecisionRouter(new StubClient("""
            {"answers":{"branch_0":{"type":"noul","noul":0.2},"branch_1":{"type":"noul","noul":0.5}}}
            """));
        var result = await router.RouteAsync("Unrelated", Branches, Config(false) with { MinConfidence = 0 }, default);
        result.NeedsClarification.Should().BeTrue();
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("missing", true)]
    [InlineData("writer", false)]
    public async Task BelowThreshold_UsesOnlyAllowedFallbackOrClarifies(string? fallback, bool clarify)
    {
        var router = new ScenarioFlowDecisionRouter(new StubClient(Choice()));
        var result = await router.RouteAsync("Unclear", Branches,
            Config() with { MinConfidence = 0.95, FallbackPersonaId = fallback }, default);
        result.NeedsClarification.Should().Be(clarify);
        if (!clarify) result.SelectedPersonaIds.Should().Equal("writer");
    }

    [Fact]
    public async Task ExplicitNoMatch_Clarifies()
    {
        var router = new ScenarioFlowDecisionRouter(new StubClient("""
            {"answers":{"route":{"type":"choice","choice":"no_match","probabilities":{"branch_0":0.1,"branch_1":0.1,"no_match":0.8}}}}
            """));
        (await router.RouteAsync("Unrelated", Branches, Config(), default)).NeedsClarification.Should().BeTrue();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"answers\":{\"route\":{\"type\":\"choice\",\"choice\":\"intruder\"}}}")]
    [InlineData("{\"answers\":{\"route\":{\"type\":\"choice\",\"choice\":\"branch_0\",\"probabilities\":{\"branch_0\":2,\"branch_1\":0,\"no_match\":0}}}}")]
    [InlineData("{\"answers\":{\"route\":{\"type\":\"choice\",\"choice\":\"branch_0\",\"probabilities\":{\"branch_0\":0.1,\"branch_1\":0.8,\"no_match\":0.1}}}}")]
    public async Task MalformedResponse_DoesNotExecuteFallback(string response)
    {
        var result = await new ScenarioFlowDecisionRouter(new StubClient(response)).RouteAsync("x", Branches,
            Config() with { FallbackPersonaId = "writer" }, default);
        result.Ok.Should().BeFalse();
        result.SelectedPersonaIds.Should().BeNull();
    }

    [Fact]
    public async Task MissingModelOrTooManyChoices_DoesNotCallProvider()
    {
        var client = new StubClient(Choice());
        var router = new ScenarioFlowDecisionRouter(client);
        (await router.RouteAsync("x", Branches, Config() with { DecisionModel = "" }, default)).Ok.Should().BeFalse();
        var many = Enumerable.Range(0, 26).Select(i => new ScenarioDecisionBranch($"p{i}", "x")).ToArray();
        (await router.RouteAsync("x", many, Config(), default)).Ok.Should().BeFalse();
        client.Model.Should().BeNull();
    }

    [Fact]
    public async Task CancellationPropagates_TimeoutAndHttpFailureReturnErrors()
    {
        var client = new Mock<IOllamaDecisionClient>();
        client.Setup(c => c.DecideAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var router = new ScenarioFlowDecisionRouter(client.Object);
        (await router.RouteAsync("x", Branches, Config(), default)).Error.Should().Contain("timed out");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => router.RouteAsync("x", Branches, Config(), cts.Token));
        client.Setup(c => c.DecideAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Ollama unavailable"));
        (await router.RouteAsync("x", Branches, Config(), default)).Error.Should().Be("Ollama unavailable");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FlowExecution_UsesDedicatedModel_InBothInterpreters(bool segmented)
    {
        var client = new StubClient(Choice());
        var loader = new Mock<IProjectLoader>();
        loader.Setup(l => l.LoadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoadedProjectContext { AgentSpecs = [new() { Id = "reader", Description = "Reads records" }] });
        var llm = new Mock<IProjectMemoryLlmClient>(MockBehavior.Strict);
        var service = new ScenarioFlowRouterLlmService(loader.Object, llm.Object,
            NullLogger<ScenarioFlowRouterLlmService>.Instance, new ScenarioFlowDecisionRouter(client));
        var flow = Flow();
        var selected = new List<string>();
        var interpreter = new ScenarioFlowGraphInterpreter();
        Task<string> Invoke(string persona, string prompt, CancellationToken ct, string? node)
        {
            selected.Add(persona);
            return Task.FromResult("Answer");
        }
        if (segmented)
            await interpreter.ExecuteSegmentAsync(flow, "in", new(), new(), new(), "Read records", Invoke,
                TimeSpan.FromSeconds(10), "/project", service);
        else
            await interpreter.ExecuteAsync(flow, "Read records", Invoke, TimeSpan.FromSeconds(10), "/project", service);
        selected.Should().Equal("reader");
        client.Model.Should().Be("tev1:4b-q4_K_M");
        client.Questions.ToString().Should().Contain("Reads records").And.Contain("Look up existing records");
        llm.VerifyNoOtherCalls();
    }

    [Fact]
    public void ValidationAndSerialization_PreserveDecisionConfig_AndAllowInheritedModel()
    {
        var flow = JsonSerializer.Deserialize<ScenarioFlowDocument>(JsonSerializer.Serialize(Flow()))!;
        var scenario = new ScenarioDefinition { Id = "decision", PersonaAgentIds = ["writer", "reader"], Flow = flow };
        ScenarioFlowValidator.Validate(scenario).Should().BeEmpty();
        var config = ScenarioFlowRouterConfig.Parse(flow.Nodes[1].Config);
        config.Mode.Should().Be(ScenarioFlowRouterMode.Decision);
        config.DecisionModel.Should().Be("tev1:4b-q4_K_M");
        flow.Nodes[1].Config = Json("{\"routerMode\":\"decision\",\"routerTargetPolicy\":\"single_best\"}");
        ScenarioFlowValidator.Validate(scenario).Should().BeEmpty();
    }

    private static ScenarioFlowDocument Flow() => new()
    {
        SchemaVersion = "1.0", GraphId = "decision-test",
        Nodes = [new() { Id = "in", Type = "ChatInput" },
            new() { Id = "router", Type = "Router", Config = Json("{\"routerMode\":\"decision\",\"decisionModel\":\"tev1:4b-q4_K_M\",\"routerTargetPolicy\":\"single_best\"}") },
            new() { Id = "write", Type = "LlmNode", Config = Json("{\"personaId\":\"writer\"}") },
            new() { Id = "read", Type = "LlmNode", Config = Json("{\"personaId\":\"reader\"}") },
            new() { Id = "out", Type = "Output" }],
        Edges = [new() { Id = "e0", FromNodeId = "in", ToNodeId = "router", Mode = "sequential" },
            new() { Id = "e1", FromNodeId = "router", ToNodeId = "write", Mode = "sequential" },
            new() { Id = "e2", FromNodeId = "router", ToNodeId = "read", Mode = "sequential", LlmRoutingHint = "Look up existing records" },
            new() { Id = "e3", FromNodeId = "write", ToNodeId = "out", Mode = "sequential" },
            new() { Id = "e4", FromNodeId = "read", ToNodeId = "out", Mode = "sequential" }]
    };

    private sealed class StubClient(string response) : IOllamaDecisionClient
    {
        public string? Model { get; private set; }
        public string? State { get; private set; }
        public JsonElement Questions { get; private set; }
        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<JsonElement> DecideAsync(string model, string state, IReadOnlyDictionary<string, object> questions, CancellationToken cancellationToken)
        {
            Model = model; State = state; Questions = JsonSerializer.SerializeToElement(questions);
            return Task.FromResult(Json(response));
        }
    }
}
