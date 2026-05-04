using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Adapters;
using AgctorSDK.Core.DependencyInjection;
using AgctorSDK.Core.ProjectMemory.Loading;
using AgctorSDK.Core.ProjectMemory.Orchestration;
using AgctorSDK.Core.ProjectMemory.Orchestration.Actors;
using AgctorSDK.Core.ProjectMemory.OutOfSchema;
using AgctorSDK.Core.ProjectMemory.Tools;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgctorSDK.Core.Tests.ProjectMemory;

public sealed class ProjectMemoryWorkflowActorTests
{
    private static string RepoRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static void CopyDir(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, dir);
            Directory.CreateDirectory(Path.Combine(dest, rel));
        }

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, file);
            var target = Path.Combine(dest, rel);
            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.Copy(file, target, overwrite: true);
        }
    }

    [Fact]
    public async Task ReceiveAsync_Delegates_To_Pipeline_And_Returns_Result()
    {
        var expected = new ProjectMemoryPipelineResult
        {
            CorrelationId = "corr-workflow",
            Success = true,
            FinalText = "done",
            Steps = new[]
            {
                new ProjectMemoryPipelineStep { Name = "query", Ok = true, Detail = "done" }
            }
        };
        var runner = new FakeRunner(expected);
        var actor = new ProjectMemoryWorkflowActor("pm-workflow", runner);
        await actor.InitializeAsync();

        var request = new ProjectMemoryPipelineRequest
        {
            ProjectRoot = "/tmp/project",
            UserMessage = "hello",
            CorrelationId = "corr-workflow"
        };
        var envelope = AgctorEnvelopeBuilder.Request(
            new ProjectMemoryWorkflowRequest(request),
            senderId: "test",
            receiverId: actor.Id,
            correlationId: "corr-workflow");

        var response = await actor.ReceiveAsync(envelope);

        runner.LastRequest.Should().BeSameAs(request);
        response.GetMessageType().Should().Be(AgctorMessageTypes.Result);
        response.GetCorrelationId().Should().Be("corr-workflow");
        response.Payload.Should().BeOfType<ProjectMemoryWorkflowResult>()
            .Which.PipelineResult.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task ReceiveAsync_Returns_Error_For_Unsupported_Payload()
    {
        var actor = new ProjectMemoryWorkflowActor("pm-workflow", new FakeRunner(new ProjectMemoryPipelineResult()));
        await actor.InitializeAsync();

        var envelope = AgctorEnvelopeBuilder.Request(
            "not-a-workflow-request",
            senderId: "test",
            receiverId: actor.Id,
            correlationId: "corr-error");

        var response = await actor.ReceiveAsync(envelope);

        response.GetMessageType().Should().Be(AgctorMessageTypes.ErrorResponse);
        response.GetCorrelationId().Should().Be("corr-error");
    }

    [Fact]
    public async Task IngestActor_Delegates_To_Pipeline_Ingest_Api()
    {
        var runner = new FakeRunner(new ProjectMemoryPipelineResult())
        {
            IngestResult = new ProjectMemoryIngestResult { ParseSuccess = true, Summary = "ok" }
        };
        var actor = new ProjectMemoryIngestActor("pm-ingest", runner);
        await actor.InitializeAsync();

        var envelope = AgctorEnvelopeBuilder.Request(
            new ProjectMemoryIngestWorkflowRequest("/tmp/project", "scenario-1", "{}"),
            senderId: "test",
            receiverId: actor.Id,
            correlationId: "corr-ingest");

        var response = await actor.ReceiveAsync(envelope);

        runner.LastIngestArgs.Should().Be(("/tmp/project", "scenario-1", "{}"));
        response.GetMessageType().Should().Be(AgctorMessageTypes.Result);
        response.GetCorrelationId().Should().Be("corr-ingest");
        response.Payload.Should().BeOfType<ProjectMemoryIngestWorkflowResult>()
            .Which.IngestResult.ParseSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task GenericInboxActor_Delegates_To_Pipeline_Persist_Api()
    {
        var runner = new FakeRunner(new ProjectMemoryPipelineResult())
        {
            PersistResult = new GenericInboxPersistResult { Appended = 1 }
        };
        var actor = new ProjectMemoryGenericInboxActor("pm-generic-inbox", runner);
        await actor.InitializeAsync();
        var approvals = new List<ApprovedGenericFact>
        {
            new()
        };

        var envelope = AgctorEnvelopeBuilder.Request(
            new ProjectMemoryGenericInboxPersistRequest("/tmp/project", "scenario-1", approvals),
            senderId: "test",
            receiverId: actor.Id,
            correlationId: "corr-generic");

        var response = await actor.ReceiveAsync(envelope);

        runner.LastPersistArgs.Should().NotBeNull();
        runner.LastPersistArgs!.Value.projectRoot.Should().Be("/tmp/project");
        runner.LastPersistArgs!.Value.scenarioId.Should().Be("scenario-1");
        runner.LastPersistArgs!.Value.approvals.Should().BeSameAs(approvals);
        response.GetMessageType().Should().Be(AgctorMessageTypes.Result);
        response.GetCorrelationId().Should().Be("corr-generic");
        response.Payload.Should().BeOfType<ProjectMemoryGenericInboxPersistResult>()
            .Which.PersistResult.Appended.Should().Be(1);
    }

    [Fact]
    public async Task ExtractActor_Uses_Loaded_Yaml_Instructions_And_Shared_Llm_Client()
    {
        var projectRoot = Path.Combine(RepoRoot(), "samples", "people-project");
        var llm = new QueueLlm("""{"memoryIntents":[]}""");
        var actor = new ProjectMemoryExtractActor("pm-extract", new ProjectLoader(), llm);
        await actor.InitializeAsync();

        var envelope = AgctorEnvelopeBuilder.Request(
            new ProjectMemoryExtractWorkflowRequest(projectRoot, "Raha likes walking.", "Earlier context"),
            senderId: "test",
            receiverId: actor.Id,
            correlationId: "corr-extract");

        var response = await actor.ReceiveAsync(envelope);

        response.GetMessageType().Should().Be(AgctorMessageTypes.Result);
        response.GetCorrelationId().Should().Be("corr-extract");
        var result = response.Payload.Should().BeOfType<ProjectMemoryExtractWorkflowResult>().Subject;
        result.RawExtractorLlmText.Should().Be("""{"memoryIntents":[]}""");
        result.Prompt.Should().Contain("Identify person entities mentioned in the input.");
        result.Prompt.Should().Contain("Prior conversation:");
        llm.LastPrompt.Should().Be(result.Prompt);
    }

    [Fact]
    public async Task QueryActor_Builds_Context_From_ProjectMemory_Operations_And_Uses_Llm_Client()
    {
        var projectRoot = Path.Combine(RepoRoot(), "samples", "people-project");
        var loader = new ProjectLoader();
        var entities = new EntityRegistry();
        var ops = new ProjectMemoryOperations(loader, entities);
        var llm = new QueueLlm("query-answer");
        var actor = new ProjectMemoryQueryActor("pm-query", loader, ops, llm);
        await actor.InitializeAsync();

        var envelope = AgctorEnvelopeBuilder.Request(
            new ProjectMemoryQueryWorkflowRequest(projectRoot, "Who is Raha?", "Earlier context", "person_1"),
            senderId: "test",
            receiverId: actor.Id,
            correlationId: "corr-query");

        var response = await actor.ReceiveAsync(envelope);

        response.GetMessageType().Should().Be(AgctorMessageTypes.Result);
        response.GetCorrelationId().Should().Be("corr-query");
        var result = response.Payload.Should().BeOfType<ProjectMemoryQueryWorkflowResult>().Subject;
        result.Answer.Should().Be("query-answer");
        result.Prompt.Should().Contain("Context:");
        result.Prompt.Should().Contain("### raha");
        result.Prompt.Should().Contain("Question:");
        llm.LastPrompt.Should().Be(result.Prompt);
    }

    [Fact]
    public async Task ActorBackedRunner_Routes_RunAsync_Through_Runtime_Workflow_Actor()
    {
        using var runtime = new InMemoryActorRuntime();
        await runtime.InitializeAsync(new Dictionary<string, object>());
        var expected = new ProjectMemoryPipelineResult
        {
            CorrelationId = "corr-runner",
            Success = true,
            FinalText = "actor-backed"
        };
        var direct = new FakeRunner(expected);
        var runner = new ActorBackedProjectMemoryPipelineRunner(runtime, direct);
        var request = new ProjectMemoryPipelineRequest
        {
            ProjectRoot = "/tmp/project",
            UserMessage = "hello",
            CorrelationId = "corr-runner"
        };

        var result = await runner.RunAsync(request);

        result.Should().BeSameAs(expected);
        direct.LastRequest.Should().BeSameAs(request);
        (await runtime.GetActorAsync<ProjectMemoryWorkflowActor>("project-memory:workflow")).Should().NotBeNull();
    }

    [Fact]
    public async Task ActorBackedRunner_Routes_Ingest_And_GenericInbox_Through_Runtime_Actors()
    {
        using var runtime = new InMemoryActorRuntime();
        await runtime.InitializeAsync(new Dictionary<string, object>());
        var approvals = new List<ApprovedGenericFact> { new() { ProposalId = "p1" } };
        var direct = new FakeRunner(new ProjectMemoryPipelineResult())
        {
            IngestResult = new ProjectMemoryIngestResult { ParseSuccess = true },
            PersistResult = new GenericInboxPersistResult { Appended = 1 }
        };
        var runner = new ActorBackedProjectMemoryPipelineRunner(runtime, direct);

        var ingest = await runner.IngestFromExtractorOutputAsync("/tmp/project", "scenario", "{}");
        var persisted = await runner.PersistApprovedGenericFactsAsync("/tmp/project", "scenario", approvals);

        ingest.Should().BeSameAs(direct.IngestResult);
        persisted.Should().BeSameAs(direct.PersistResult);
        direct.LastIngestArgs.Should().Be(("/tmp/project", "scenario", "{}"));
        direct.LastPersistArgs!.Value.approvals.Should().BeSameAs(approvals);
        (await runtime.GetActorAsync<ProjectMemoryIngestActor>("project-memory:ingest")).Should().NotBeNull();
        (await runtime.GetActorAsync<ProjectMemoryGenericInboxActor>("project-memory:generic-inbox")).Should().NotBeNull();
    }

    [Fact]
    public async Task ActorBackedRunner_Parity_Covers_Phase3_Scenarios()
    {
        var src = Path.Combine(RepoRoot(), "samples", "people-project");
        var directRoot = Path.Combine(Path.GetTempPath(), "pm-phase3-direct-" + Guid.NewGuid().ToString("N"));
        var actorRoot = Path.Combine(Path.GetTempPath(), "pm-phase3-actor-" + Guid.NewGuid().ToString("N"));
        CopyDir(src, directRoot);
        CopyDir(src, actorRoot);

        try
        {
            var llmDirect = new QueueLlm(
                // ingest-only prompt
                """{"memoryIntents":[{"entityKey":"raha","knowledgeType":"profile_fact","attribute":"age","value":"45","confidence":0.99}]}""",
                // query-only prompt
                "query-only answer",
                // auto mode: extract + query
                """{"memoryIntents":[{"entityKey":"raha","knowledgeType":"profile_fact","attribute":"occupation","value":"Engineer","confidence":0.99}]}""",
                "auto answer",
                // route miss to immediate confirmation
                """{"memoryIntents":[{"entityKey":"raha","knowledgeType":"pets","attribute":"dogs","value":"two dogs","confidence":0.95}]}"""
            );
            var llmActor = new QueueLlm(
                """{"memoryIntents":[{"entityKey":"raha","knowledgeType":"profile_fact","attribute":"age","value":"45","confidence":0.99}]}""",
                "query-only answer",
                """{"memoryIntents":[{"entityKey":"raha","knowledgeType":"profile_fact","attribute":"occupation","value":"Engineer","confidence":0.99}]}""",
                "auto answer",
                """{"memoryIntents":[{"entityKey":"raha","knowledgeType":"pets","attribute":"dogs","value":"two dogs","confidence":0.95}]}"""
            );

            var directRunner = BuildDirectRunner(llmDirect);
            using var runtime = new InMemoryActorRuntime();
            await runtime.InitializeAsync(new Dictionary<string, object>());
            var actorRunner = new ActorBackedProjectMemoryPipelineRunner(runtime, BuildDirectRunner(llmActor));

            // ingest-only parity
            var ingestRequestDirect = new ProjectMemoryPipelineRequest
            {
                ProjectRoot = directRoot,
                UserMessage = "Raha is 45.",
                Mode = ProjectMemoryPipelineMode.IngestOnly
            };
            var ingestRequestActor = new ProjectMemoryPipelineRequest
            {
                ProjectRoot = actorRoot,
                UserMessage = "Raha is 45.",
                Mode = ProjectMemoryPipelineMode.IngestOnly
            };
            var ingestDirect = await directRunner.RunAsync(ingestRequestDirect);
            var ingestActor = await actorRunner.RunAsync(ingestRequestActor);
            ingestActor.Success.Should().Be(ingestDirect.Success);
            ingestActor.Steps.Select(s => s.Name).Should().BeEquivalentTo(ingestDirect.Steps.Select(s => s.Name), opts => opts.WithStrictOrdering());

            // query-only parity
            var queryDirect = await directRunner.RunAsync(new ProjectMemoryPipelineRequest
            {
                ProjectRoot = directRoot,
                UserMessage = "Who is Raha?",
                Mode = ProjectMemoryPipelineMode.QueryOnly
            });
            var queryActor = await actorRunner.RunAsync(new ProjectMemoryPipelineRequest
            {
                ProjectRoot = actorRoot,
                UserMessage = "Who is Raha?",
                Mode = ProjectMemoryPipelineMode.QueryOnly
            });
            queryActor.FinalText.Should().Be(queryDirect.FinalText);

            // auto mode parity
            var autoDirect = await directRunner.RunAsync(new ProjectMemoryPipelineRequest
            {
                ProjectRoot = directRoot,
                UserMessage = "Raha occupation update and answer.",
                Mode = ProjectMemoryPipelineMode.Auto
            });
            var autoActor = await actorRunner.RunAsync(new ProjectMemoryPipelineRequest
            {
                ProjectRoot = actorRoot,
                UserMessage = "Raha occupation update and answer.",
                Mode = ProjectMemoryPipelineMode.Auto
            });
            autoActor.FinalText.Should().Be(autoDirect.FinalText);
            autoActor.Success.Should().Be(autoDirect.Success);

            // parse-failure parity for ingest API
            var parseFailDirect = await directRunner.IngestFromExtractorOutputAsync(directRoot, null, "{bad-json");
            var parseFailActor = await actorRunner.IngestFromExtractorOutputAsync(actorRoot, null, "{bad-json");
            parseFailActor.ParseSuccess.Should().Be(parseFailDirect.ParseSuccess);
            parseFailActor.WroteAnyFile.Should().Be(parseFailDirect.WroteAnyFile);

            // route-miss + immediate confirmation parity
            var routeMissDirect = await directRunner.RunAsync(new ProjectMemoryPipelineRequest
            {
                ProjectRoot = directRoot,
                UserMessage = "Raha has two dogs.",
                Mode = ProjectMemoryPipelineMode.IngestOnly
            });
            var routeMissActor = await actorRunner.RunAsync(new ProjectMemoryPipelineRequest
            {
                ProjectRoot = actorRoot,
                UserMessage = "Raha has two dogs.",
                Mode = ProjectMemoryPipelineMode.IngestOnly
            });
            routeMissActor.Success.Should().Be(routeMissDirect.Success);
            routeMissActor.FinalText.Should().Be(routeMissDirect.FinalText);
            routeMissActor.Steps.Select(s => s.Name).Should().BeEquivalentTo(routeMissDirect.Steps.Select(s => s.Name), opts => opts.WithStrictOrdering());

            var confirmDirect = await directRunner.RunAsync(new ProjectMemoryPipelineRequest
            {
                ProjectRoot = directRoot,
                UserMessage = "store this fact",
                Mode = ProjectMemoryPipelineMode.IngestOnly
            });
            var confirmActor = await actorRunner.RunAsync(new ProjectMemoryPipelineRequest
            {
                ProjectRoot = actorRoot,
                UserMessage = "store this fact",
                Mode = ProjectMemoryPipelineMode.IngestOnly
            });
            confirmActor.Success.Should().Be(confirmDirect.Success);
            confirmActor.FinalText.Should().Be(confirmDirect.FinalText);

            // generic-inbox persistence parity
            var proposalsDirect = await directRunner.IngestFromExtractorOutputAsync(
                directRoot,
                scenarioId: null,
                """{"memoryIntents":[{"entityKey":"raha","knowledgeType":"pets","attribute":"cats","value":"one cat","confidence":0.5}]}"""
            );
            var proposalsActor = await actorRunner.IngestFromExtractorOutputAsync(
                actorRoot,
                scenarioId: null,
                """{"memoryIntents":[{"entityKey":"raha","knowledgeType":"pets","attribute":"cats","value":"one cat","confidence":0.5}]}"""
            );
            proposalsDirect.OutOfSchemaProposals.Should().HaveCount(1);
            proposalsActor.OutOfSchemaProposals.Should().HaveCount(1);

            var approvedDirect = new[]
            {
                new ApprovedGenericFact
                {
                    ProposalId = proposalsDirect.OutOfSchemaProposals[0].ProposalId,
                    EntityKey = proposalsDirect.OutOfSchemaProposals[0].EntityKey,
                    KnowledgeType = proposalsDirect.OutOfSchemaProposals[0].KnowledgeType,
                    Attribute = proposalsDirect.OutOfSchemaProposals[0].Attribute,
                    Value = proposalsDirect.OutOfSchemaProposals[0].Value,
                    Confidence = proposalsDirect.OutOfSchemaProposals[0].Confidence
                }
            };
            var approvedActor = new[]
            {
                new ApprovedGenericFact
                {
                    ProposalId = proposalsActor.OutOfSchemaProposals[0].ProposalId,
                    EntityKey = proposalsActor.OutOfSchemaProposals[0].EntityKey,
                    KnowledgeType = proposalsActor.OutOfSchemaProposals[0].KnowledgeType,
                    Attribute = proposalsActor.OutOfSchemaProposals[0].Attribute,
                    Value = proposalsActor.OutOfSchemaProposals[0].Value,
                    Confidence = proposalsActor.OutOfSchemaProposals[0].Confidence
                }
            };

            var persistDirect = await directRunner.PersistApprovedGenericFactsAsync(directRoot, null, approvedDirect);
            var persistActor = await actorRunner.PersistApprovedGenericFactsAsync(actorRoot, null, approvedActor);
            persistActor.Appended.Should().Be(persistDirect.Appended);
            persistActor.RejectedMismatch.Should().Be(persistDirect.RejectedMismatch);
        }
        finally
        {
            try { Directory.Delete(directRoot, recursive: true); } catch { }
            try { Directory.Delete(actorRoot, recursive: true); } catch { }
        }
    }

    private static IProjectMemoryPipelineRunner BuildDirectRunner(IProjectMemoryLlmClient llm)
    {
        var services = new ServiceCollection();
        services.AddAgctorProjectMemory();
        services.AddSingleton<IProjectMemoryLlmClient>(llm);
        services.AddSingleton<IProjectMemoryPipelineRunner, ProjectMemoryPipelineRunner>();
        return services.BuildServiceProvider().GetRequiredService<IProjectMemoryPipelineRunner>();
    }

    private sealed class FakeRunner : IProjectMemoryPipelineRunner
    {
        private readonly ProjectMemoryPipelineResult _result;

        public FakeRunner(ProjectMemoryPipelineResult result)
        {
            _result = result;
        }

        public ProjectMemoryPipelineRequest? LastRequest { get; private set; }
        public (string projectRoot, string? scenarioId, string rawExtractorLlmText)? LastIngestArgs { get; private set; }
        public (string projectRoot, string? scenarioId, IReadOnlyList<ApprovedGenericFact> approvals)? LastPersistArgs { get; private set; }
        public ProjectMemoryIngestResult IngestResult { get; init; } = new();
        public GenericInboxPersistResult PersistResult { get; init; } = new();

        public Task<ProjectMemoryPipelineResult> RunAsync(ProjectMemoryPipelineRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(_result);
        }

        public Task<ProjectMemoryIngestResult> IngestFromExtractorOutputAsync(
            string projectRoot,
            string? scenarioId,
            string rawExtractorLlmText,
            CancellationToken cancellationToken = default)
        {
            LastIngestArgs = (projectRoot, scenarioId, rawExtractorLlmText);
            return Task.FromResult(IngestResult);
        }

        public Task<GenericInboxPersistResult> PersistApprovedGenericFactsAsync(
            string projectRoot,
            string? scenarioId,
            IReadOnlyList<ApprovedGenericFact> approvals,
            CancellationToken cancellationToken = default)
        {
            LastPersistArgs = (projectRoot, scenarioId, approvals);
            return Task.FromResult(PersistResult);
        }
    }

    private sealed class QueueLlm : IProjectMemoryLlmClient
    {
        private readonly Queue<string> _responses = new();

        public QueueLlm(params string[] responses)
        {
            foreach (var response in responses)
                _responses.Enqueue(response);
        }

        public string? LastPrompt { get; private set; }

        public Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
        {
            LastPrompt = prompt;
            return Task.FromResult(_responses.Count == 0 ? "" : _responses.Dequeue());
        }
    }
}

