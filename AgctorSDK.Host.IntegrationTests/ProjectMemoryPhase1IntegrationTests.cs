using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgctorSDK.Core.Sessions.Models;
using AgctorSDK.Host.Models;
using Microsoft.Extensions.Configuration;

namespace AgctorSDK.Host.IntegrationTests;

/// <summary>Phase 1: people scenario routing, focus person on chat projects, scenario entities API.</summary>
public sealed class ProjectMemoryPhase1IntegrationTests : IClassFixture<AgctorWebApplicationFactory>
{
    private readonly HttpClient _client;
    private static int _portCounter = 17420;

    public ProjectMemoryPhase1IntegrationTests(AgctorWebApplicationFactory factory)
    {
        var configured = factory.WithWebHostBuilder(builder =>
        {
            // Use repo user catalog so merged "people" flow (router + query branch) is available.
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
    public async Task ListAgents_IncludesRelationshipCoach_AfterYamlFix()
    {
        var res = await _client.GetAsync("/api/project-memory/agents");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var ids = doc.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("id").GetString())
            .Where(s => s != null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        ids.Should().Contain("relationship-coach");
        ids.Should().Contain("person-query");
        ids.Should().Contain("person-extractor");
    }

    [Fact]
    public async Task ScenarioEntities_ForPeople_ReturnsSamplePeople()
    {
        var res = await _client.GetAsync("/api/project-memory/scenario-entities?scenarioId=people");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().BeGreaterThan(0);
        var keys = doc.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("entityKey").GetString())
            .Where(k => k != null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        keys.Should().Contain("ryan");
    }

    [Fact]
    public async Task ChatProject_FocusEntity_PersistsAndLists()
    {
        var create = await _client.PostAsJsonAsync("/api/chat/projects", new CreateChatProjectRequest
        {
            Name = "Phase1 Focus Test",
            ScenarioId = "people",
            FocusEntityKey = "raha",
            FocusDisplayName = "Raha Mohebbi"
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var project = await create.Content.ReadFromJsonAsync<SessionProject>();
        project.Should().NotBeNull();
        project!.FocusEntityKey.Should().Be("raha");
        project.FocusDisplayName.Should().Be("Raha Mohebbi");

        var get = await _client.GetAsync($"/api/chat/projects/{project.ProjectId}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var loaded = await get.Content.ReadFromJsonAsync<SessionProject>();
        loaded!.FocusEntityKey.Should().Be("raha");
    }

    [Fact]
    public async Task ScenariosCatalog_PeopleFlow_HasQueryAndRouterBranches()
    {
        var res = await _client.GetAsync("/api/scenarios/people");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await res.Content.ReadAsStringAsync();
        json.Should().Contain("person-query");
        json.Should().Contain("routerMode");
        json.Should().Contain("n_people_query");
    }

    [Fact]
    public async Task PlaygroundPage_ReturnsOk()
    {
        var res = await _client.GetAsync("/Dashboard/ProjectMemory/Playground");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
