using AgctorSDK.Core.Messages;
using AgctorSDK.Core.ProjectMemory.Scenarios;
using AgctorSDK.Core.ProjectMemory.Scenarios.Actors;
using AgctorSDK.Core.ProjectMemory.Scenarios.Messages;
using FluentAssertions;
using Xunit;

namespace AgctorSDK.Core.Tests.ProjectMemory;

public sealed class ScenarioFlowRuntimeActorTests
{
    private const string MinimalAwaitFlowJson = """
        {"schemaVersion":"2.0","graphId":"t","nodes":[
          {"id":"n_await","type":"AwaitEvent","config":{"eventType":"visual.extract.completed"}},
          {"id":"out1","type":"Output"}
        ],"edges":[
          {"id":"e1","fromNodeId":"n_await","toNodeId":"out1","mode":"sequential"}
        ]}
        """;

    [Fact]
    public async Task StartFlow_Suspends_On_WaitForInput_Segment()
    {
        var store = new InMemoryScenarioFlowRuntimeStore();
        var executor = new FakeSegmentExecutor(ScenarioFlowSegmentOutcome.SuspendedWaitForInput, "ask-photos", "Upload photos");
        var actor = new ScenarioFlowRuntimeActor("runtime-1", store, executor);
        await actor.InitializeAsync();

        var result = await actor.ReceiveAsync(AgctorEnvelopeBuilder.Request(
            new ScenarioFlowStartMessage("sess-1", "people", "/tmp/p", "flow-1", "{}", "style advice?", Array.Empty<string>(), "corr-1"),
            "test",
            actor.Id,
            "corr-1"));

        result.GetMessageType().Should().Be(AgctorMessageTypes.Result);
        var payload = result.Payload.Should().BeOfType<ScenarioFlowRuntimeResult>().Subject;
        payload.Success.Should().BeTrue();
        payload.Completed.Should().BeFalse();
        payload.Status.Should().Be(ScenarioFlowRuntimeStatus.WaitingForUserInput);
        payload.ExecutionNodeId.Should().Be("ask-photos");
        payload.PendingPrompt.Should().Be("Upload photos");

        var saved = await store.LoadAsync("/tmp/p", "sess-1", "people");
        saved.Should().NotBeNull();
        saved!.Status.Should().Be(ScenarioFlowRuntimeStatus.WaitingForUserInput);
    }

    [Fact]
    public async Task ResumeUserInput_Completes_After_Segment()
    {
        var store = new InMemoryScenarioFlowRuntimeStore();
        var executor = new FakeSegmentExecutor(ScenarioFlowSegmentOutcome.SuspendedWaitForInput, "ask-photos", "Upload photos");
        var actor = new ScenarioFlowRuntimeActor("runtime-1", store, executor);
        await actor.InitializeAsync();

        await actor.ReceiveAsync(AgctorEnvelopeBuilder.Request(
            new ScenarioFlowStartMessage("sess-1", "people", "/tmp/p", "flow-1", "{}", "style?", Array.Empty<string>(), "c1"),
            "test", actor.Id, "c1"));

        executor.NextOutcome = ScenarioFlowSegmentOutcome.Completed;
        executor.NextOutput = "Wear earth tones.";

        var resume = await actor.ReceiveAsync(AgctorEnvelopeBuilder.Request(
            new ScenarioFlowResumeUserInputMessage("sess-1", "people", "/tmp/p", "flow-1", "{}", "here are photos", new[] { "asset-1" }, "c2"),
            "test", actor.Id, "c2"));

        var payload = resume.Payload.Should().BeOfType<ScenarioFlowRuntimeResult>().Subject;
        payload.Completed.Should().BeTrue();
        payload.Output.Should().Be("Wear earth tones.");
    }

    [Fact]
    public async Task StartFlow_Suspends_On_AwaitEvent_Segment()
    {
        var store = new InMemoryScenarioFlowRuntimeStore();
        var executor = new FakeSegmentExecutor(ScenarioFlowSegmentOutcome.SuspendedAwaitEvent, "n_await", null);
        var actor = new ScenarioFlowRuntimeActor("runtime-1", store, executor);
        await actor.InitializeAsync();

        var result = await actor.ReceiveAsync(AgctorEnvelopeBuilder.Request(
            new ScenarioFlowStartMessage("sess-1", "people", "/tmp/p", "flow-1", "{}", "analyze photos", Array.Empty<string>(), "c1"),
            "test", actor.Id, "c1"));

        var payload = result.Payload.Should().BeOfType<ScenarioFlowRuntimeResult>().Subject;
        payload.Success.Should().BeTrue();
        payload.Completed.Should().BeFalse();
        payload.Status.Should().Be(ScenarioFlowRuntimeStatus.WaitingForDomainEvent);
        payload.ExecutionNodeId.Should().Be("n_await");

        var saved = await store.LoadAsync("/tmp/p", "sess-1", "people");
        saved!.Status.Should().Be(ScenarioFlowRuntimeStatus.WaitingForDomainEvent);
    }

    [Fact]
    public async Task ResumeDomainEvent_MergesFacts_And_Completes_Segment()
    {
        var store = new InMemoryScenarioFlowRuntimeStore();
        var executor = new FakeSegmentExecutor(ScenarioFlowSegmentOutcome.SuspendedAwaitEvent, "n_await", null);
        var actor = new ScenarioFlowRuntimeActor("runtime-1", store, executor);
        await actor.InitializeAsync();

        await actor.ReceiveAsync(AgctorEnvelopeBuilder.Request(
            new ScenarioFlowStartMessage("sess-1", "people", "/tmp/p", "flow-1", "{}", "photos", Array.Empty<string>(), "c1"),
            "test", actor.Id, "c1"));

        executor.NextOutcome = ScenarioFlowSegmentOutcome.Completed;
        executor.NextOutput = "Curated memories applied.";
        executor.NextExecutionNodeId = "out1";

        var resume = await actor.ReceiveAsync(AgctorEnvelopeBuilder.Request(
            new ScenarioFlowResumeDomainEventMessage(
                "sess-1",
                "people",
                "/tmp/p",
                "flow-1",
                MinimalAwaitFlowJson,
                ScenarioFlowDomainEventTypes.VisualExtractCompleted,
                new Dictionary<string, object?> { ["visual.hasPhotos"] = true },
                "c2"),
            "test", actor.Id, "c2"));

        var payload = resume.Payload.Should().BeOfType<ScenarioFlowRuntimeResult>().Subject;
        payload.Completed.Should().BeTrue();
        payload.Output.Should().Be("Curated memories applied.");

        var saved = await store.LoadAsync("/tmp/p", "sess-1", "people");
        saved!.Store.Facts["visual.hasPhotos"].Should().Be(true);
        saved.Status.Should().Be(ScenarioFlowRuntimeStatus.Completed);
    }

    [Fact]
    public async Task StartWithAttachments_AutoAdvances_Past_AskForPhotos_WaitForInput()
    {
        var store = new InMemoryScenarioFlowRuntimeStore();
        var flowJson = """
            {"schemaVersion":"2.0","graphId":"t","nodes":[
              {"id":"in1","type":"ChatInput","label":"In"},
              {"id":"ask","type":"WaitForInput","label":"Ask","config":{"acceptAttachments":true,"promptTemplate":"Upload photos"}},
              {"id":"ingest","type":"LlmNode","label":"Ingest","config":{"personaId":"visual-intake"}},
              {"id":"out1","type":"Output","label":"Out"}
            ],"edges":[
              {"id":"e1","fromNodeId":"in1","toNodeId":"ask","mode":"sequential"},
              {"id":"loop","fromNodeId":"ask","toNodeId":"ingest","mode":"loopBack"},
              {"id":"e2","fromNodeId":"ingest","toNodeId":"out1","mode":"sequential"}
            ]}
            """;
        var executor = new AttachmentsThenCompleteExecutor();
        var actor = new ScenarioFlowRuntimeActor("runtime-1", store, executor);
        await actor.InitializeAsync();

        var result = await actor.ReceiveAsync(AgctorEnvelopeBuilder.Request(
            new ScenarioFlowStartMessage("sess-1", "people", "/tmp/p", "flow-1", flowJson, "style advice", new[] { "asset-1" }, "c1"),
            "test", actor.Id, "c1"));

        var payload = result.Payload.Should().BeOfType<ScenarioFlowRuntimeResult>().Subject;
        payload.Completed.Should().BeTrue();
        executor.CallCount.Should().Be(2, "segment runs twice: ask then auto-resume to ingest");
        executor.SecondPassStartNode.Should().Be("ingest");
    }

    [Fact]
    public async Task StartWithAttachments_sets_interim_text_when_suspended_on_await_event()
    {
        var store = new InMemoryScenarioFlowRuntimeStore();
        var flowJson = """
            {"schemaVersion":"2.0","graphId":"t","nodes":[
              {"id":"ask","type":"WaitForInput","label":"Ask","config":{"acceptAttachments":true,"promptTemplate":"Upload photos"}},
              {"id":"n_visual","type":"LlmNode","label":"Visual","config":{"personaId":"visual-intake"}},
              {"id":"await","type":"AwaitEvent","label":"Await","config":{"eventType":"visual.extract.completed"}},
              {"id":"out1","type":"Output","label":"Out"}
            ],"edges":[
              {"id":"loop","fromNodeId":"ask","toNodeId":"n_visual","mode":"loopBack"},
              {"id":"e1","fromNodeId":"n_visual","toNodeId":"await","mode":"sequential"},
              {"id":"e2","fromNodeId":"await","toNodeId":"out1","mode":"sequential"}
            ]}
            """;
        var executor = new AttachmentsThenAwaitExecutor();
        var actor = new ScenarioFlowRuntimeActor("runtime-1", store, executor);
        await actor.InitializeAsync();

        var result = await actor.ReceiveAsync(AgctorEnvelopeBuilder.Request(
            new ScenarioFlowStartMessage("sess-1", "people", "/tmp/p", "flow-1", flowJson, "photos", new[] { "asset-1" }, "c1"),
            "test", actor.Id, "c1"));

        var payload = result.Payload.Should().BeOfType<ScenarioFlowRuntimeResult>().Subject;
        payload.Status.Should().Be(ScenarioFlowRuntimeStatus.WaitingForDomainEvent);
        payload.Output.Should().Contain("analyzing");
        payload.PendingPrompt.Should().Contain("analyzing");
    }

    [Fact]
    public async Task Photo_loop_multi_turn_start_resume_domain_event_path()
    {
        var store = new InMemoryScenarioFlowRuntimeStore();
        var executor = new PhotoLoopFakeSegmentExecutor();
        var actor = new ScenarioFlowRuntimeActor("runtime-1", store, executor);
        await actor.InitializeAsync();

        var start = await actor.ReceiveAsync(AgctorEnvelopeBuilder.Request(
            new ScenarioFlowStartMessage("sess-1", "people-style-photo-loop", "/tmp/p", "flow-1", "{}", "style advice?", Array.Empty<string>(), "c1"),
            "test", actor.Id, "c1"));
        start.Payload.Should().BeOfType<ScenarioFlowRuntimeResult>().Which.Status
            .Should().Be(ScenarioFlowRuntimeStatus.WaitingForUserInput);

        var resumeUser = await actor.ReceiveAsync(AgctorEnvelopeBuilder.Request(
            new ScenarioFlowResumeUserInputMessage("sess-1", "people-style-photo-loop", "/tmp/p", "flow-1", "{}", "here are photos", new[] { "asset-1" }, "c2"),
            "test", actor.Id, "c2"));
        resumeUser.Payload.Should().BeOfType<ScenarioFlowRuntimeResult>().Which.Status
            .Should().Be(ScenarioFlowRuntimeStatus.WaitingForDomainEvent);

        var resumeEvent = await actor.ReceiveAsync(AgctorEnvelopeBuilder.Request(
            new ScenarioFlowResumeDomainEventMessage(
                "sess-1",
                "people-style-photo-loop",
                "/tmp/p",
                "flow-1",
                MinimalAwaitFlowJson,
                ScenarioFlowDomainEventTypes.VisualExtractCompleted,
                new Dictionary<string, object?> { ["visual.hasPhotos"] = true },
                "c3"),
            "test", actor.Id, "c3"));

        var final = resumeEvent.Payload.Should().BeOfType<ScenarioFlowRuntimeResult>().Subject;
        final.Completed.Should().BeTrue();
        final.Output.Should().Contain("earth tones");
    }

    [Fact]
    public async Task ResumeDomainEvent_AdvancesPastAwaitEvent_instead_of_reSuspending()
    {
        var store = new InMemoryScenarioFlowRuntimeStore();
        var flowJson = """
            {"schemaVersion":"2.0","graphId":"t","nodes":[
              {"id":"await","type":"AwaitEvent","label":"Await","config":{"eventType":"visual.extract.completed"}},
              {"id":"curate","type":"LlmNode","label":"Curate","config":{"personaId":"memory-curator"}},
              {"id":"out1","type":"Output","label":"Out"}
            ],"edges":[
              {"id":"e1","fromNodeId":"await","toNodeId":"curate","mode":"sequential"},
              {"id":"e2","fromNodeId":"curate","toNodeId":"out1","mode":"sequential"}
            ]}
            """;
        var executor = new DomainEventResumeTrackingExecutor();
        var actor = new ScenarioFlowRuntimeActor("runtime-1", store, executor);
        await actor.InitializeAsync();

        var snapshot = new ScenarioFlowRuntimeSnapshot
        {
            FlowId = "t",
            ExecutionNodeId = "await",
            Status = ScenarioFlowRuntimeStatus.WaitingForDomainEvent,
            AwaitingEvent = new ScenarioFlowAwaitingEventState { EventType = "visual.extract.completed" }
        };
        await store.SaveAsync("/tmp/p", "sess-1", "people", snapshot);

        var resume = await actor.ReceiveAsync(AgctorEnvelopeBuilder.Request(
            new ScenarioFlowResumeDomainEventMessage(
                "sess-1",
                "people",
                "/tmp/p",
                "t",
                flowJson,
                ScenarioFlowDomainEventTypes.VisualExtractCompleted,
                new Dictionary<string, object?> { ["visual.hasPhotos"] = true },
                "c-resume"),
            "test",
            actor.Id,
            "c-resume"));

        executor.LastStartNodeId.Should().Be("curate", "domain-event resume must not re-enter AwaitEvent");
        var payload = resume.Payload.Should().BeOfType<ScenarioFlowRuntimeResult>().Subject;
        payload.Completed.Should().BeTrue();
        payload.Output.Should().Be("Curated and done.");
    }

    private sealed class DomainEventResumeTrackingExecutor : IScenarioFlowSegmentExecutor
    {
        public string? LastStartNodeId { get; private set; }

        public Task<ScenarioFlowSegmentResult> RunSegmentAsync(ScenarioFlowSegmentRequest request, CancellationToken cancellationToken = default)
        {
            LastStartNodeId = request.Snapshot.ExecutionNodeId;
            var snap = request.Snapshot;
            snap.ExecutionNodeId = "out1";
            return Task.FromResult(new ScenarioFlowSegmentResult
            {
                Outcome = ScenarioFlowSegmentOutcome.Completed,
                Snapshot = snap,
                Output = "Curated and done."
            });
        }
    }

    private sealed class AttachmentsThenCompleteExecutor : IScenarioFlowSegmentExecutor
    {
        public int CallCount { get; private set; }

        public string? SecondPassStartNode { get; private set; }

        public Task<ScenarioFlowSegmentResult> RunSegmentAsync(ScenarioFlowSegmentRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            var snap = request.Snapshot;
            if (CallCount == 1)
            {
                return Task.FromResult(new ScenarioFlowSegmentResult
                {
                    Outcome = ScenarioFlowSegmentOutcome.SuspendedWaitForInput,
                    Snapshot = WithNode(snap, "ask", "Upload photos"),
                    Output = null
                });
            }

            SecondPassStartNode = snap.ExecutionNodeId;
            return Task.FromResult(new ScenarioFlowSegmentResult
            {
                Outcome = ScenarioFlowSegmentOutcome.Completed,
                Snapshot = WithNode(snap, "out1", null),
                Output = "Photos ingested."
            });
        }

        private static ScenarioFlowRuntimeSnapshot WithNode(ScenarioFlowRuntimeSnapshot snap, string nodeId, string? prompt)
        {
            snap.ExecutionNodeId = nodeId;
            snap.PendingPrompt = prompt;
            return snap;
        }
    }

    private sealed class AttachmentsThenAwaitExecutor : IScenarioFlowSegmentExecutor
    {
        public int CallCount { get; private set; }

        public Task<ScenarioFlowSegmentResult> RunSegmentAsync(ScenarioFlowSegmentRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            var snap = request.Snapshot;
            if (CallCount == 1)
            {
                return Task.FromResult(new ScenarioFlowSegmentResult
                {
                    Outcome = ScenarioFlowSegmentOutcome.SuspendedWaitForInput,
                    Snapshot = WithNode(snap, "ask", "Upload photos"),
                    Output = null
                });
            }

            snap.Store.NodeOutputs["n_visual"] = new ScenarioFlowNodeOutputState
            {
                Text = "Thanks for the photos — analyzing them now."
            };
            snap.ExecutionNodeId = "await";
            return Task.FromResult(new ScenarioFlowSegmentResult
            {
                Outcome = ScenarioFlowSegmentOutcome.SuspendedAwaitEvent,
                Snapshot = snap,
                Output = null
            });
        }

        private static ScenarioFlowRuntimeSnapshot WithNode(ScenarioFlowRuntimeSnapshot snap, string nodeId, string? prompt)
        {
            snap.ExecutionNodeId = nodeId;
            snap.PendingPrompt = prompt;
            return snap;
        }
    }

    private sealed class InMemoryScenarioFlowRuntimeStore : IScenarioFlowRuntimeStore
    {
        private readonly Dictionary<string, ScenarioFlowRuntimeSnapshot> _data = new();

        private static string Key(string projectRoot, string sessionId, string scenarioId) =>
            $"{projectRoot}|{sessionId}|{scenarioId}";

        public Task<ScenarioFlowRuntimeSnapshot?> LoadAsync(string projectRoot, string sessionId, string scenarioId, CancellationToken cancellationToken = default)
        {
            _data.TryGetValue(Key(projectRoot, sessionId, scenarioId), out var snap);
            return Task.FromResult(snap);
        }

        public Task SaveAsync(string projectRoot, string sessionId, string scenarioId, ScenarioFlowRuntimeSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            _data[Key(projectRoot, sessionId, scenarioId)] = snapshot;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string projectRoot, string sessionId, string scenarioId, CancellationToken cancellationToken = default)
        {
            _data.Remove(Key(projectRoot, sessionId, scenarioId));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSegmentExecutor : IScenarioFlowSegmentExecutor
    {
        public FakeSegmentExecutor(ScenarioFlowSegmentOutcome outcome, string executionNodeId, string? pendingPrompt)
        {
            NextOutcome = outcome;
            NextExecutionNodeId = executionNodeId;
            NextPendingPrompt = pendingPrompt;
        }

        public ScenarioFlowSegmentOutcome NextOutcome { get; set; }
        public string NextExecutionNodeId { get; set; }
        public string? NextPendingPrompt { get; set; }
        public string? NextOutput { get; set; }

        public Task<ScenarioFlowSegmentResult> RunSegmentAsync(ScenarioFlowSegmentRequest request, CancellationToken cancellationToken = default)
        {
            var snap = request.Snapshot;
            snap.ExecutionNodeId = NextExecutionNodeId;
            snap.PendingPrompt = NextPendingPrompt;

            return Task.FromResult(new ScenarioFlowSegmentResult
            {
                Outcome = NextOutcome,
                Snapshot = snap,
                Output = NextOutput
            });
        }
    }

    /// <summary>Simulates style-photo-loop: ask → await extract → complete.</summary>
    private sealed class PhotoLoopFakeSegmentExecutor : IScenarioFlowSegmentExecutor
    {
        private int _call;

        public Task<ScenarioFlowSegmentResult> RunSegmentAsync(ScenarioFlowSegmentRequest request, CancellationToken cancellationToken = default)
        {
            _call++;
            var snap = request.Snapshot;
            return Task.FromResult(_call switch
            {
                1 => new ScenarioFlowSegmentResult
                {
                    Outcome = ScenarioFlowSegmentOutcome.SuspendedWaitForInput,
                    Snapshot = WithNode(snap, "n_ask", "Please upload photos."),
                    Output = null
                },
                2 => new ScenarioFlowSegmentResult
                {
                    Outcome = ScenarioFlowSegmentOutcome.SuspendedAwaitEvent,
                    Snapshot = WithNode(snap, "n_await", null),
                    Output = null
                },
                _ => new ScenarioFlowSegmentResult
                {
                    Outcome = ScenarioFlowSegmentOutcome.Completed,
                    Snapshot = WithNode(snap, "out1", null),
                    Output = "Wear earth tones for your next outfit."
                }
            });
        }

        private static ScenarioFlowRuntimeSnapshot WithNode(ScenarioFlowRuntimeSnapshot snap, string nodeId, string? prompt)
        {
            snap.ExecutionNodeId = nodeId;
            snap.PendingPrompt = prompt;
            return snap;
        }
    }
}
