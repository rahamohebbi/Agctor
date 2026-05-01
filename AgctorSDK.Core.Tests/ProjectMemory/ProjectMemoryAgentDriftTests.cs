using System.Text.Json;
using AgctorSDK.Agents.ProjectMemory;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Tools;
using FluentAssertions;
using Xunit;

namespace AgctorSDK.Core.Tests.ProjectMemory;

public sealed class ProjectMemoryAgentDriftTests
{
    [Fact]
    public async Task PersonExtractor_Uses_ExtractorSpec_Instructions_In_Prompt()
    {
        var services = new FakeProjectMemoryAgentServices
        {
            ProjectRoot = "/tmp/project",
            LoadedContext = BuildContext(new AgentDefinitionSpec
            {
                Id = "person-extractor",
                Instructions = new List<string> { "EXTRACTOR-SENTINEL", "Second line" }
            }),
            LlmResponse = """{"memoryIntents":[]}"""
        };
        var agent = new PersonExtractorProjectAgent("extractor-1", services);

        var response = await agent.ReceiveAsync(new MessageEnvelope("input text"));

        response.Payload.Should().Be("""{"memoryIntents":[]}""");
        services.LastPrompt.Should().Contain("EXTRACTOR-SENTINEL");
        services.LastPrompt.Should().Contain("Input:\ninput text");
    }

    [Fact]
    public async Task PersonQuery_Uses_QuerySpec_Instructions_And_Context_In_Prompt()
    {
        var services = new FakeProjectMemoryAgentServices
        {
            ProjectRoot = "/tmp/project",
            LoadedContext = BuildContext(new AgentDefinitionSpec
            {
                Id = "person-query",
                Instructions = new List<string> { "QUERY-SENTINEL" }
            }),
            LlmResponse = "query-answer"
        };
        services.SearchHits.Add(new EntitySearchHit("raha", "person", "/tmp/project/people/raha"));
        services.Documents["people/raha/profile.md"] = "Raha profile facts";

        var agent = new PersonQueryProjectAgent("query-1", services);
        var response = await agent.ReceiveAsync(new MessageEnvelope("Who is Raha?"));

        response.Payload.Should().Be("query-answer");
        services.LastPrompt.Should().Contain("QUERY-SENTINEL");
        services.LastPrompt.Should().Contain("### raha");
        services.LastPrompt.Should().Contain("Raha profile facts");
        services.LastPrompt.Should().Contain("Question:\nWho is Raha?");
    }

    [Fact]
    public async Task MemoryCurator_Uses_Route_Discover_And_Projection_Services()
    {
        var services = new FakeProjectMemoryAgentServices
        {
            ProjectRoot = "/tmp/project",
            LoadedContext = BuildContext(new AgentDefinitionSpec
            {
                Id = "memory-curator",
                Instructions = new List<string> { "CURATOR-SENTINEL" }
            })
        };
        services.DiscoveredEntities.Add(new EntityRecord
        {
            EntityKey = "raha",
            EntityType = "person",
            RootPath = "/tmp/project/people/raha"
        });
        services.RoutedIntents.Add(new RoutedMemoryIntent
        {
            Original = new MemoryIntent
            {
                EntityKey = "raha",
                KnowledgeType = "profile_fact",
                Attribute = "age",
                Value = "45",
                Confidence = 0.9
            },
            FileName = "profile.md",
            DocumentTypeId = "profile",
            SectionTitle = "Basic Info"
        });

        var batchJson = JsonSerializer.Serialize(new MemoryIntentBatch
        {
            MemoryIntents = new List<MemoryIntent>
            {
                new()
                {
                    EntityKey = "raha",
                    KnowledgeType = "profile_fact",
                    Attribute = "age",
                    Value = "45",
                    Confidence = 0.9
                }
            }
        });

        var agent = new MemoryCuratorProjectAgent("curator-1", services);
        var response = await agent.ReceiveAsync(new MessageEnvelope(batchJson));

        services.RouteCalled.Should().BeTrue();
        services.DiscoverCalled.Should().BeTrue();
        services.ApplyProjectionCalled.Should().BeTrue();
        response.Payload.Should().BeOfType<string>().Which.Should().Contain("updatedFiles");
    }

    private static LoadedProjectContext BuildContext(params AgentDefinitionSpec[] specs) =>
        new()
        {
            ProjectRoot = "/tmp/project",
            AgentSpecs = specs
        };

    private sealed class FakeProjectMemoryAgentServices : IProjectMemoryAgentServices
    {
        public string? ProjectRoot { get; set; }
        public LoadedProjectContext LoadedContext { get; set; } = new();
        public string LlmResponse { get; set; } = "";
        public string LastPrompt { get; private set; } = "";
        public List<EntitySearchHit> SearchHits { get; } = new();
        public Dictionary<string, string> Documents { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<RoutedMemoryIntent> RoutedIntents { get; } = new();
        public List<ValidationIssue> RouteIssues { get; } = new();
        public List<EntityRecord> DiscoveredEntities { get; } = new();

        public bool RouteCalled { get; private set; }
        public bool DiscoverCalled { get; private set; }
        public bool ApplyProjectionCalled { get; private set; }

        public string? GetProjectRoot() => ProjectRoot;

        public Task<LoadedProjectContext> LoadProjectAsync(string root, CancellationToken cancellationToken) =>
            Task.FromResult(LoadedContext);

        public Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken)
        {
            LastPrompt = prompt;
            return Task.FromResult(LlmResponse);
        }

        public Task<IReadOnlyList<EntitySearchHit>> SearchEntitiesAsync(string projectRoot, string? query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<EntitySearchHit>>(SearchHits);

        public Task<string> ReadDocumentAsync(AgentDefinitionSpec spec, string projectRoot, string relativePath, CancellationToken cancellationToken) =>
            Task.FromResult(Documents.TryGetValue(relativePath, out var content) ? content : "");

        public IReadOnlyList<RoutedMemoryIntent> Route(LoadedProjectContext ctx, IReadOnlyList<MemoryIntent> intents, out IReadOnlyList<ValidationIssue> issues)
        {
            RouteCalled = true;
            issues = RouteIssues;
            return RoutedIntents;
        }

        public Task<IReadOnlyList<EntityRecord>> DiscoverAsync(LoadedProjectContext ctx, string entityWorkspaceRoot, CancellationToken cancellationToken)
        {
            DiscoverCalled = true;
            return Task.FromResult<IReadOnlyList<EntityRecord>>(DiscoveredEntities);
        }

        public Task<ProjectionResult> ApplyProjectionAsync(EntityRecord entity, IReadOnlyList<RoutedMemoryIntent> intents, CancellationToken cancellationToken)
        {
            ApplyProjectionCalled = true;
            var result = new ProjectionResult();
            result.UpdatedFiles.Add("people/raha/profile.md");
            return Task.FromResult(result);
        }
    }
}
