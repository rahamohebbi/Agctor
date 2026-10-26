using System.Net;
using System.Text;
using System.Text.Json;
using AgctorSDK.Core.Ollama;
using AgctorSDK.Core.Rag;
using AgctorSDK.Core.ProjectMemory.Scenarios;
using AgctorSDK.Host.Services;
using AgctorSDK.Host.Services.Models;
using AgctorSDK.Host.Services.Scenarios;
using AgctorSDK.Host.Services.Sessions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Moq;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class ModelSelectionTests
{
    private static ScenarioDefinition Scenario() => new()
    {
        Id = "test", ModelDefaults = new() { Generation = "scenario-llm", LlmRouting = "router-llm", DecisionRouting = "scenario-decision" },
        Flow = new() { GraphId = "g", Nodes = [
            new() { Id = "answer", Type = "LlmNode", Config = JsonSerializer.SerializeToElement(new { personaId = "p", llmModel = "node-llm" }) },
            new() { Id = "decision", Type = "Router", Config = JsonSerializer.SerializeToElement(new { routerMode = "decision" }) },
            new() { Id = "router", Type = "Router", Config = JsonSerializer.SerializeToElement(new { routerMode = "llm" }) }
        ] }
    };
    [Fact]
    public void RagSelection_InheritsAndNormalizes_AndNoneIsAnExplicitOverride()
    {
        var scenario = Scenario();
        var settings = new SessionModelSettings { ScenarioId = scenario.Id };
        ModelSelectionPlan Resolve() => ModelSelectionService.Resolve(scenario, settings, "app", globalRagProvider: RagProviderIds.Cognee);
        Resolve().RagProvider.Should().Be(new RagProviderSelection(RagProviderIds.Cognee, "global default"));
        scenario.RagProviderId = " light-rag ";
        Resolve().RagProvider.Should().Be(new RagProviderSelection(RagProviderIds.LightRag, "scenario default"));
        settings.RagProviderId = "graphiti";
        var snapshot = Resolve();
        snapshot.RagProvider.Should().Be(new RagProviderSelection(RagProviderIds.Graphiti, "chat override"));
        settings.RagProviderId = RagProviderIds.None;
        Resolve().RagProvider!.ProviderId.Should().Be(RagProviderIds.None);
        snapshot.RagProvider!.ProviderId.Should().Be(RagProviderIds.Graphiti);
        settings.ScenarioId = "other";
        Resolve().RagProvider!.ProviderId.Should().Be(RagProviderIds.LightRag);
    }

    [Fact]
    public void Priority_IsNodeChatThenNodeThenChatThenScenarioThenApplication()
    {
        var scenario = Scenario();
        var settings = new SessionModelSettings { ScenarioId = "test", Defaults = new() { Generation = "chat-llm" } };
        var plan = ModelSelectionService.Resolve(scenario, settings, "app");
        plan.Nodes["answer"].Should().Be(new ResolvedModel(ModelRoles.Generation, "node-llm", "flow node"));
        settings.Nodes["answer"] = new(ModelRoles.Generation, "chat-node");
        ModelSelectionService.Resolve(scenario, settings, "app").Nodes["answer"].Source.Should().Be("chat node override");
        settings.Nodes.Clear(); scenario.Flow!.Nodes[0].Config = JsonSerializer.SerializeToElement(new { personaId = "p" });
        ModelSelectionService.Resolve(scenario, settings, "app").Nodes["answer"].Model.Should().Be("chat-llm");
        settings.Defaults.Generation = null;
        ModelSelectionService.Resolve(scenario, settings, "app").Nodes["answer"].Model.Should().Be("scenario-llm");
        scenario.ModelDefaults.Generation = null;
        ModelSelectionService.Resolve(scenario, settings, "app").Nodes["answer"].Model.Should().Be("app");
        // Existing snapshots remain unchanged by later settings edits.
        plan.Nodes["answer"].Model.Should().Be("node-llm");
    }
    [Fact]
    public void RolesAndScenarioBoundaries_DoNotCrossOverride()
    {
        var scenario = Scenario();
        var settings = new SessionModelSettings { ScenarioId = "test", Defaults = new() { Generation = "chat" } };
        settings.Nodes["decision"] = new(ModelRoles.Generation, "wrong-capability");
        var plan = ModelSelectionService.Resolve(scenario, settings, "app");
        plan.Nodes["decision"].Model.Should().Be("scenario-decision");
        plan.Nodes["router"].Model.Should().Be("router-llm");
        settings.ScenarioId = "other";
        ModelSelectionService.Resolve(scenario, settings, "app").Defaults[ModelRoles.Generation].Model.Should().Be("scenario-llm");
        ModelSelectionService.Resolve(null, null, "app").Defaults[ModelRoles.DecisionRouting].Model.Should().BeNull();
    }
    [Fact]
    public async Task ConcurrentBranchesAndChats_KeepIndependentScopes_AndRestoreOnFailure()
    {
        var plan = ModelSelectionService.Resolve(Scenario(), null, "app");
        using var outer = ModelSelectionScope.Begin(plan);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<string> Branch(string model)
        {
            using var inner = ModelSelectionScope.Select(new(ModelRoles.Generation, model, "test"));
            await ready.Task;
            await Task.Yield();
            return ModelSelectionScope.GenerationModel;
        }
        var a = Branch("a"); var b = Branch("b"); ready.SetResult();
        (await Task.WhenAll(a, b)).Should().Equal("a", "b");
        ModelSelectionScope.GenerationModel.Should().Be("scenario-llm");
        try { using var failed = ModelSelectionScope.Select(new(ModelRoles.Generation, "failed", "test")); throw new Exception(); } catch { }
        ModelSelectionScope.GenerationModel.Should().Be("scenario-llm");
    }
    [Fact]
    public void ActorEnvelope_AndPersistedSnapshot_PreservePlan_WithoutChangingSavedGraph()
    {
        var scenario = Scenario();
        scenario.RagProviderId = RagProviderIds.Graphiti;
        var plan = ModelSelectionService.Resolve(scenario, null, "app");
        var envelope = ModelSelectionService.ExecutionJson(scenario.Flow!, plan);
        var restored = ModelSelectionService.ReadExecutionPlan(envelope)!;
        using (ModelSelectionScope.Begin(restored))
        using (ModelSelectionScope.Node("ANSWER", ModelRoles.Generation))
            ModelSelectionScope.GenerationModel.Should().Be("node-llm");
        var snapshot = new ScenarioFlowRuntimeSnapshot { ModelPlan = restored };
        var roundTrip = JsonSerializer.Deserialize<ScenarioFlowRuntimeSnapshot>(JsonSerializer.Serialize(snapshot))!;
        roundTrip.ModelPlan!.RagProvider.Should().Be(new RagProviderSelection(RagProviderIds.Graphiti, "scenario default"));
        roundTrip.ModelPlan!.Nodes["decision"].Model.Should().Be("scenario-decision");
        JsonSerializer.Serialize(scenario.Flow).Should().NotContain("_modelPlan");
    }
    [Fact]
    public async Task SessionOverrides_PersistAcrossStoreReload_StayIsolated_AndDeleteWithSession()
    {
        var path = Path.Combine(Path.GetTempPath(), "model-selection-" + Guid.NewGuid() + ".db");
        try
        {
            var store = new SqliteSessionStore(path);
            await store.CreateSessionAsync("one"); await store.CreateSessionAsync("two");
            var settings = new SessionModelSettings { ScenarioId = "test", RagProviderId = RagProviderIds.Cognee, Defaults = new() { Generation = "session-llm" } };
            await store.SaveModelSettingsJsonAsync("one", JsonSerializer.Serialize(settings, ModelSelectionService.Json));
            var catalog = new Mock<IScenarioCatalog>(); catalog.Setup(c => c.Get("test")).Returns(Scenario());
            var restarted = new SqliteSessionStore(path);
            var service = new ModelSelectionService(restarted, catalog.Object, new ConfigurationBuilder().Build());
            (await service.SettingsAsync("one", "test", default)).Defaults.Generation.Should().Be("session-llm");
            (await service.PlanAsync("one", "test", default)).RagProvider!.ProviderId.Should().Be(RagProviderIds.Cognee);
            (await service.SettingsAsync("two", "test", default)).RagProviderId.Should().BeNull();
            (await service.SettingsAsync("two", "test", default)).Defaults.Generation.Should().BeNull();
            (await service.SettingsAsync("one", "another-scenario", default)).Defaults.Generation.Should().BeNull();
            await store.SaveModelSettingsJsonAsync("one", JsonSerializer.Serialize(new SessionModelSettings { ScenarioId = "test" }, ModelSelectionService.Json));
            (await service.SettingsAsync("one", "test", default)).Defaults.Generation.Should().BeNull();
            (await service.SettingsAsync("one", "test", default)).RagProviderId.Should().BeNull();
            await store.DeleteSessionAsync("one");
            (await restarted.GetModelSettingsJsonAsync("one")).Should().BeNull();
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }
    [Fact]
    public async Task CapabilityValidation_RejectsWrongTypeUnavailableAndUnsetDecision()
    {
        var clients = new Mock<IHttpClientFactory>();
        clients.Setup(c => c.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(new CapabilityHandler()));
        var service = new ModelCapabilitiesService(clients.Object, Mock.Of<IOllamaModelCatalog>());
        await service.ValidateAsync([new(ModelRoles.Generation, "chat", "test"), new(ModelRoles.DecisionRouting, "decision", "test")], default);
        await ((Func<Task>)(() => service.ValidateAsync([new(ModelRoles.DecisionRouting, "chat", "test")], default))).Should().ThrowAsync<InvalidOperationException>();
        await ((Func<Task>)(() => service.ValidateAsync([new(ModelRoles.Generation, "decision", "test")], default))).Should().ThrowAsync<InvalidOperationException>();
        await ((Func<Task>)(() => service.ValidateAsync([new(ModelRoles.Generation, "missing", "test")], default))).Should().ThrowAsync<InvalidOperationException>();
        await ((Func<Task>)(() => service.ValidateAsync([new(ModelRoles.DecisionRouting, null, "test")], default))).Should().ThrowAsync<InvalidOperationException>();
    }
    [Fact]
    public async Task Interpreter_UsesResolvedDecisionAndPersonaModels_InBothExecutionPaths()
    {
        var scenario = new ScenarioDefinition
        {
            Id = "routed", ModelDefaults = new() { Generation = "scenario-chat", DecisionRouting = "scenario-decision" },
            Flow = new() { GraphId = "routed", Nodes = [
                new() { Id = "in", Type = "ChatInput" },
                new() { Id = "router", Type = "Router", Config = JsonSerializer.SerializeToElement(new { routerMode = "decision", routerTargetPolicy = "single_best" }) },
                new() { Id = "answer", Type = "LlmNode", Config = JsonSerializer.SerializeToElement(new { personaId = "p", llmModel = "pinned-chat" }) },
                new() { Id = "out", Type = "Output" }
            ], Edges = [
                new() { Id = "a", FromNodeId = "in", ToNodeId = "router", Mode = "sequential" },
                new() { Id = "b", FromNodeId = "router", ToNodeId = "answer", Mode = "sequential" },
                new() { Id = "c", FromNodeId = "answer", ToNodeId = "out", Mode = "sequential" }
            ] }
        };
        var settings = new SessionModelSettings { ScenarioId = "routed", Nodes = new() { ["answer"] = new(ModelRoles.Generation, "chat-node") } };
        var plan = ModelSelectionService.Resolve(scenario, settings, "application");
        var router = new Mock<IScenarioFlowRouterLlmService>();
        router.Setup(r => r.RouteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ScenarioFlowRouterPersonaCandidate>>(), It.IsAny<ScenarioFlowRouterConfig>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
            .Returns((string root, string message, IReadOnlyList<ScenarioFlowRouterPersonaCandidate> candidates, ScenarioFlowRouterConfig config, CancellationToken ct, string context) =>
            {
                config.DecisionModel.Should().Be("scenario-decision");
                ModelSelectionScope.Selection!.Role.Should().Be(ModelRoles.DecisionRouting);
                return Task.FromResult(ScenarioFlowRouterLlmResult.Success(["p"]));
            });
        Task<string> Invoke(string persona, string prompt, CancellationToken ct, string? node)
        {
            ModelSelectionScope.GenerationModel.Should().Be("chat-node");
            ModelSelectionScope.Selection!.Source.Should().Be("chat node override");
            return Task.FromResult("answer");
        }
        using (ModelSelectionScope.Begin(plan))
        {
            var interpreter = new ScenarioFlowGraphInterpreter();
            (await interpreter.ExecuteAsync(scenario.Flow, "hello", Invoke, TimeSpan.FromSeconds(10), "/test", router.Object)).Should().Contain("answer");
            await interpreter.ExecuteSegmentAsync(scenario.Flow, "in", new(), new(), new ScenarioFlowRuntimeSnapshot(), "hello", Invoke, TimeSpan.FromSeconds(10), "/test", router.Object);
        }
        router.Verify(r => r.RouteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ScenarioFlowRouterPersonaCandidate>>(), It.IsAny<ScenarioFlowRouterConfig>(), It.IsAny<CancellationToken>(), It.IsAny<string>()), Times.Exactly(2));
    }

    private sealed class CapabilityHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var model = body.RootElement.GetProperty("model").GetString();
            return new(model == "missing" ? HttpStatusCode.NotFound : HttpStatusCode.OK)
            { Content = new StringContent(JsonSerializer.Serialize(new { capabilities = new[] { model == "decision" ? "decision" : "completion" } }), Encoding.UTF8, "application/json") };
        }
    }
}
