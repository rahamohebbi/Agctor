using System.Text.Json;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.ProjectMemory.Scenarios;
using AgctorSDK.Core.ProjectMemory.Scenarios.Actors;
using AgctorSDK.Core.ProjectMemory.Scenarios.Messages;
using AgctorSDK.Host.Services.ProjectMemory;
using AgctorSDK.Host.Services.Scenarios;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace AgctorSDK.Host.IntegrationTests;
/// <summary>PRD-024 Phase E: catalog template <c>people-style-photo-loop</c> validates and uses runtime actor.</summary>
public sealed class ScenarioFlowStylePhotoLoopTests : IClassFixture<AgctorWebApplicationFactory>
{
    private readonly AgctorWebApplicationFactory _factory;

    public ScenarioFlowStylePhotoLoopTests(AgctorWebApplicationFactory factory) => _factory = factory;

    private static ScenarioDefinition LoadTemplateDefinition(IWebHostEnvironment env)
    {
        var userFile = Path.Combine(env.ContentRootPath, "Config", "agctor-scenarios.user.json");
        var json = File.ReadAllText(userFile);
        var doc = JsonSerializer.Deserialize<ScenarioCatalogDocument>(json, ScenarioFlowJson.Options);
        var scenario = doc?.Scenarios?.FirstOrDefault(s =>
            string.Equals(s.Id, "people-style-photo-loop", StringComparison.OrdinalIgnoreCase));
        scenario.Should().NotBeNull("people-style-photo-loop must exist in Config/agctor-scenarios.user.json");
        return scenario!;
    }

    [Fact]
    public void Catalog_people_style_photo_loop_validates_and_requires_runtime_actor()
    {
        using var scope = _factory.Services.CreateScope();
        var env = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
        var def = LoadTemplateDefinition(env);

        var errors = ScenarioFlowValidator.Validate(def);
        errors.Should().BeEmpty(string.Join("; ", errors));

        var flow = def.Flow!;
        flow.SchemaVersion.Should().Be("2.0");
        flow.Nodes.Should().Contain(n => n.Type == "Gate");
        flow.Nodes.Should().Contain(n => n.Type == "WaitForInput");
        flow.Edges.Should().Contain(e => string.Equals(e.Mode, "loopBack", StringComparison.OrdinalIgnoreCase));

        ScenarioFlowCapabilities.RequiresRuntimeActor(
            flow.SchemaVersion,
            flow.Nodes.Select(n => n.Type),
            flow.Edges.Select(e => e.Mode)).Should().BeTrue();
    }

    [Fact]
    public void Style_photo_loop_resume_from_ask_follows_photo_collection_loopBack()
    {
        using var scope = _factory.Services.CreateScope();
        var env = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
        var def = LoadTemplateDefinition(env);

        var target = ScenarioFlowGraphInterpreter.ResolveResumeTargetNode(def.Flow!, "n_ask");
        target.Should().Be("n_visual");
    }

    [Fact]
    public void Samples_flow_json_matches_catalog_template()
    {
        using var scope = _factory.Services.CreateScope();
        var env = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
        var def = LoadTemplateDefinition(env);

        var repoRoot = Path.GetFullPath(Path.Combine(env.ContentRootPath, ".."));
        var samplePath = Path.Combine(repoRoot, "samples", "people-project", ".agctor", "flows", "people-style-photo-loop.flow.json");
        File.Exists(samplePath).Should().BeTrue("sample flow file should ship with people-project");

        var sampleFlow = JsonSerializer.Deserialize<ScenarioFlowDocument>(File.ReadAllText(samplePath), ScenarioFlowJson.Options);
        sampleFlow.Should().NotBeNull();
        sampleFlow!.GraphId.Should().Be(def.Flow!.GraphId);
        sampleFlow.Nodes.Count.Should().Be(def.Flow.Nodes.Count);
        sampleFlow.Edges.Count.Should().Be(def.Flow.Edges.Count);
    }

    [Fact]
    public async Task Style_photo_loop_first_segment_reaches_wait_for_input_without_predecessor_error()
    {
        using var scope = _factory.Services.CreateScope();
        var env = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
        var def = LoadTemplateDefinition(env);
        var flow = def.Flow!;

        var chatInput = flow.Nodes.First(n => n.Type == "ChatInput").Id.Trim();
        var snapshot = new ScenarioFlowRuntimeSnapshot { Status = ScenarioFlowRuntimeStatus.Running };
        var store = ScenarioFlowGraphInterpreter.BuildTextStore(snapshot);
        var completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var interpreter = new ScenarioFlowGraphInterpreter();
        const string weddingPrompt = "advice me on my style and how I can get ready for a friend's wedding";

        var segment = await interpreter.ExecuteSegmentAsync(
            flow,
            chatInput,
            store,
            completed,
            snapshot,
            weddingPrompt,
            async (_, prompt, _, _) => await Task.FromResult($"Style advice for: {prompt}"),
            TimeSpan.FromSeconds(30),
            projectRoot: "/tmp/p",
            routerLlm: null,
            CancellationToken.None);

        segment.Outcome.Should().Be(ScenarioFlowSegmentOutcome.SuspendedWaitForInput);
        segment.Snapshot.ExecutionNodeId.Should().Be("n_ask");
        segment.Snapshot.PendingPrompt.Should().Contain("wedding");
    }

    [Fact]
    public async Task Style_photo_loop_with_attachments_skips_visual_intake_llm_and_awaits_extract()
    {
        using var scope = _factory.Services.CreateScope();
        var env = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
        var def = LoadTemplateDefinition(env);
        var flow = def.Flow!;

        var snapshot = new ScenarioFlowRuntimeSnapshot
        {
            Status = ScenarioFlowRuntimeStatus.WaitingForUserInput,
            ExecutionNodeId = "n_ask"
        };
        ScenarioFlowLoopTraversal.MergeAttachmentDelta(snapshot, new[] { "asset-photo-1" });

        var llmCalls = 0;
        var executor = new ScenarioFlowSegmentExecutor(
            new CountingFlowPersonaRunner(() => llmCalls++),
            routerLlm: null!);
        var flowJson = JsonSerializer.Serialize(flow, ScenarioFlowJson.Options);

        var segment = await executor.RunSegmentAsync(
            new ScenarioFlowSegmentRequest
            {
                ProjectRoot = "/tmp/p",
                ScenarioId = "people-style-photo-loop",
                SessionId = "sess-1",
                UserMessage = "(User attached image(s) without a caption.)",
                AttachmentIds = new[] { "asset-photo-1" },
                Snapshot = snapshot,
                FlowJson = flowJson,
                LlmNodeTimeout = TimeSpan.FromSeconds(30)
            },
            CancellationToken.None);

        llmCalls.Should().Be(0, "visual-intake should not call Ollama when attachments arrived this turn");
        segment.Outcome.Should().Be(ScenarioFlowSegmentOutcome.SuspendedAwaitEvent);
        segment.Snapshot.ExecutionNodeId.Should().Be("n_await");
        segment.Snapshot.Store.NodeOutputs.Should().ContainKey("n_visual");
        segment.Snapshot.Store.NodeOutputs["n_visual"].Text.Should().Contain("analyzing");
    }

    [Fact]
    public async Task Style_photo_loop_actor_resume_with_attachments_reaches_await_event()
    {
        using var scope = _factory.Services.CreateScope();
        var env = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
        var def = LoadTemplateDefinition(env);
        var flow = def.Flow!;
        var flowJson = JsonSerializer.Serialize(flow, ScenarioFlowJson.Options);

        var store = new InMemoryScenarioFlowRuntimeStore();
        var llmCalls = 0;
        var segmentExecutor = new ScenarioFlowSegmentExecutor(
            new CountingFlowPersonaRunner(() => llmCalls++),
            routerLlm: null!);
        var actor = new ScenarioFlowRuntimeActor("runtime-photo-loop", store, segmentExecutor);
        await actor.InitializeAsync();

        var snapshot = new ScenarioFlowRuntimeSnapshot
        {
            FlowId = flow.GraphId,
            ExecutionNodeId = "n_ask",
            Status = ScenarioFlowRuntimeStatus.WaitingForUserInput,
            PendingPrompt = "Upload photos",
            Store =
            {
                NodeOutputs =
                {
                    ["n_style"] = new ScenarioFlowNodeOutputState { Text = "Initial style advice.", Scope = "run" }
                }
            }
        };
        await store.SaveAsync("/tmp/p", "sess-actor", "people-style-photo-loop", snapshot);

        var resume = await actor.ReceiveAsync(AgctorEnvelopeBuilder.Request(
            new ScenarioFlowResumeUserInputMessage(
                "sess-actor",
                "people-style-photo-loop",
                "/tmp/p",
                flow.GraphId,
                flowJson,
                "(User attached image(s) without a caption.)",
                new[] { "asset-photo-1" },
                "c-resume"),
            "test",
            actor.Id,
            "c-resume"));

        llmCalls.Should().Be(0, "photo resume should skip visual-intake LLM and suspend on await");
        var payload = resume.Payload.Should().BeOfType<ScenarioFlowRuntimeResult>().Subject;
        payload.Status.Should().Be(ScenarioFlowRuntimeStatus.WaitingForDomainEvent);
        payload.ExecutionNodeId.Should().Be("n_await");
        payload.Output.Should().Contain("analyzing");

        var saved = await store.LoadAsync("/tmp/p", "sess-actor", "people-style-photo-loop");
        saved!.ExecutionNodeId.Should().Be("n_await");
        saved.Status.Should().Be(ScenarioFlowRuntimeStatus.WaitingForDomainEvent);
    }

    [Fact]
    public async Task Style_photo_loop_domain_event_resume_runs_style_segment_not_await_again()
    {
        using var scope = _factory.Services.CreateScope();
        var env = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
        var def = LoadTemplateDefinition(env);
        var flow = def.Flow!;
        var flowJson = JsonSerializer.Serialize(flow, ScenarioFlowJson.Options);

        var store = new InMemoryScenarioFlowRuntimeStore();
        var llmCalls = 0;
        var segmentExecutor = new ScenarioFlowSegmentExecutor(
            new CountingFlowPersonaRunner(() => llmCalls++),
            routerLlm: null!);
        var actor = new ScenarioFlowRuntimeActor("runtime-photo-loop", store, segmentExecutor);
        await actor.InitializeAsync();

        var snapshot = new ScenarioFlowRuntimeSnapshot
        {
            FlowId = flow.GraphId,
            ExecutionNodeId = "n_await",
            Status = ScenarioFlowRuntimeStatus.WaitingForDomainEvent,
            AwaitingEvent = new ScenarioFlowAwaitingEventState
            {
                EventType = ScenarioFlowDomainEventTypes.VisualExtractCompleted
            },
            Store =
            {
                NodeOutputs =
                {
                    ["n_style"] = new ScenarioFlowNodeOutputState { Text = "Initial style advice.", Scope = "run" },
                    ["n_visual"] = new ScenarioFlowNodeOutputState { Text = "Thanks for the photos.", Scope = "run" }
                },
                Facts = { ["visual.hasPhotos"] = true }
            }
        };
        await store.SaveAsync("/tmp/p", "sess-await", "people-style-photo-loop", snapshot);

        var resume = await actor.ReceiveAsync(AgctorEnvelopeBuilder.Request(
            new ScenarioFlowResumeDomainEventMessage(
                "sess-await",
                "people-style-photo-loop",
                "/tmp/p",
                flow.GraphId,
                flowJson,
                ScenarioFlowDomainEventTypes.VisualExtractCompleted,
                new Dictionary<string, object?> { ["visual.hasPhotos"] = true },
                "c-domain"),
            "test",
            actor.Id,
            "c-domain"));

        llmCalls.Should().BeGreaterThan(0, "style-coach LLM should run after extract event");
        var payload = resume.Payload.Should().BeOfType<ScenarioFlowRuntimeResult>().Subject;
        payload.Success.Should().BeTrue($"domain event resume failed: {payload.ErrorMessage}");
        payload.Completed.Should().BeTrue($"expected completed flow, status={payload.Status} node={payload.ExecutionNodeId} err={payload.ErrorMessage}");
        payload.Status.Should().Be(ScenarioFlowRuntimeStatus.Completed);

        var saved = await store.LoadAsync("/tmp/p", "sess-await", "people-style-photo-loop");
        saved!.Status.Should().Be(ScenarioFlowRuntimeStatus.Completed);
        saved.AwaitingEvent.Should().BeNull();
    }

    private sealed class InMemoryScenarioFlowRuntimeStore : IScenarioFlowRuntimeStore
    {
        private readonly Dictionary<string, ScenarioFlowRuntimeSnapshot> _runs = new(StringComparer.OrdinalIgnoreCase);

        private static string Key(string projectRoot, string sessionId, string scenarioId) =>
            $"{projectRoot}|{sessionId}|{scenarioId}";

        public Task<ScenarioFlowRuntimeSnapshot?> LoadAsync(
            string projectRoot,
            string sessionId,
            string scenarioId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_runs.TryGetValue(Key(projectRoot, sessionId, scenarioId), out var s) ? s : null);

        public Task SaveAsync(
            string projectRoot,
            string sessionId,
            string scenarioId,
            ScenarioFlowRuntimeSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            _runs[Key(projectRoot, sessionId, scenarioId)] = snapshot;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(
            string projectRoot,
            string sessionId,
            string scenarioId,
            CancellationToken cancellationToken = default)
        {
            _runs.Remove(Key(projectRoot, sessionId, scenarioId));
            return Task.CompletedTask;
        }
    }

    private sealed class CountingFlowPersonaRunner : IScenarioFlowPersonaLlmRunner
    {
        private readonly Action _onCall;

        public CountingFlowPersonaRunner(Action onCall) => _onCall = onCall;

        public Task<ProjectMemoryPersonaRunResult> RunFlowNodeAsync(
            ScenarioFlowPersonaRunRequest request,
            CancellationToken cancellationToken = default)
        {
            _onCall();
            return Task.FromResult(new ProjectMemoryPersonaRunResult(true, null, $"llm:{request.AgentId}"));
        }
    }
}