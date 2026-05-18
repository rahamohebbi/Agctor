using System.Net;
using System.Text;
using System.Text.Json;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Loading;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Tools;
using AgctorSDK.Host.Services.ProjectMemory;
using Microsoft.Extensions.Configuration;

namespace AgctorSDK.Host.IntegrationTests;

/// <summary>relationship-coach must receive scenario markdown (Ryan is preschool in person_1, not a teen).</summary>
public sealed class RelationshipCoachContextIntegrationTests : IClassFixture<AgctorWebApplicationFactory>
{
    private readonly HttpClient _client;
    private static int _portCounter = 17530;

    public RelationshipCoachContextIntegrationTests(AgctorWebApplicationFactory factory)
    {
        var configured = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((ctx, config) =>
            {
                var uniquePort = Interlocked.Increment(ref _portCounter);
                var userFile = Path.Combine(ctx.HostingEnvironment.ContentRootPath, "Config", "agctor-scenarios.user.json");
                config.AddInMemoryCollection(new[]
                {
                    new KeyValuePair<string, string?>("Mcp:Port", uniquePort.ToString()),
                    new KeyValuePair<string, string?>("Agctor:Scenarios:UserFile", userFile)
                });
            });
        });
        _client = configured.CreateClient();
    }

    [Fact]
    public async Task ShouldLoadPersonMemoryContext_IsTrue_ForRelationshipCoachFlowNode()
    {
        var root = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "samples", "people-project"));
        var loader = new ProjectLoader();
        var ctx = await loader.LoadAsync(root);
        var coach = ctx.AgentSpecs.First(a => a.Id == "relationship-coach");
        var flowConfig = JsonDocument.Parse(
            """{"personaId":"relationship-coach","contextStrategy":"markdown_all","toolIds":["person-memory-context"]}""")
            .RootElement;

        PlaygroundPersonQueryContextBuilder.ShouldLoadPersonMemoryContext(coach, flowConfig).Should().BeTrue();
    }

    [Fact]
    public async Task BuildAppendix_ForPerson1_IncludesRyanPreschoolProfile()
    {
        var root = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "samples", "people-project"));
        var loader = new ProjectLoader();
        var ctx = await loader.LoadAsync(root);
        var coach = ctx.AgentSpecs.First(a => a.Id == "relationship-coach");
        var ops = new ProjectMemoryOperations(loader, new EntityRegistry());

        var appendix = await PlaygroundPersonQueryContextBuilder.BuildAppendixAsync(
            ops,
            coach,
            root,
            "person_1",
            "markdown_all",
            "I am Ryan's dad what is important for a kid in his age?",
            CancellationToken.None);

        appendix.Should().Contain("Relationship coaching");
        appendix.Should().Contain("ryan");
        appendix.Should().Contain("Pre School", "Ryan's profile in person_1 names preschool, not teen coding");
        appendix.Should().Contain("parent: user", "user is Ryan's parent in relationships.md");
    }
}
