using System.Net;
using System.Net.Http.Json;
using System.Linq;
using System.Threading;
using AgctorSDK.Host.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace AgctorSDK.Host.IntegrationTests;

/// <summary>POST /api/scenarios/&#123;id&#125;/apply (PRD-013 Phase 4).</summary>
public sealed class ScenariosApplyIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private static int _portCounter = 15280;

    public ScenariosApplyIntegrationTests(WebApplicationFactory<Program> factory)
    {
        var configured = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var uniquePort = Interlocked.Increment(ref _portCounter);
                config.AddInMemoryCollection(new[]
                {
                    new KeyValuePair<string, string?>("Mcp:Port", uniquePort.ToString())
                });
            });
        });
        _client = configured.CreateClient();
    }

    private static ScenarioApplyRequest StubParams() =>
        new(new Dictionary<string, object> { ["useStubEmbeddings"] = true });

    [Fact]
    public async Task ApplyScenario_DefaultId_UsesConfiguredScenario_Returns200()
    {
        var res = await _client.PostAsJsonAsync("/api/scenarios/default/apply", StubParams());
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadFromJsonAsync<ScenarioSetupResponse>();
        body.Should().NotBeNull();
        body!.ScenarioName.Should().Be("code-graph-demo");
    }

    [Fact]
    public async Task ApplyScenario_ExplicitId_Returns200()
    {
        var res = await _client.PostAsJsonAsync("/api/scenarios/code-graph-demo/apply", StubParams());
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadFromJsonAsync<ScenarioSetupResponse>();
        body.Should().NotBeNull();
        body!.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ApplyScenario_UnknownId_Returns400()
    {
        var res = await _client.PostAsJsonAsync(
            "/api/scenarios/__no_such_scenario_xyz__/apply",
            StubParams());
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ListScenarios_PeopleIncludesPersonaRoster()
    {
        var list = await _client.GetFromJsonAsync<List<ScenarioDto>>("/api/scenarios");
        list.Should().NotBeNull();
        var people = list!.FirstOrDefault(x => x.Id == "people");
        people.Should().NotBeNull();
        people!.PersonaAgentIds.Should().Contain(new[] { "person-extractor", "memory-curator", "person-query" });
        people.PersonaBindings.Extractor.Should().Be("person-extractor");
    }
}
