using System.Net;
using System.Net.Http.Json;
using System.Threading;
using AgctorSDK.Host.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class ScenarioFlowRunApiTests : IClassFixture<AgctorWebApplicationFactory>
{
    private readonly HttpClient _client;
    private static int _portCounter = 16290;

    public ScenarioFlowRunApiTests(AgctorWebApplicationFactory factory)
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

    [Fact]
    public async Task FlowRun_UnknownScenario_Returns404()
    {
        var res = await _client.PostAsJsonAsync(
            "/api/scenarios/does-not-exist-xyz/flow/run",
            new ScenarioFlowRunRequest { Message = "hi" });
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task FlowRun_NoMessage_Returns400()
    {
        var res = await _client.PostAsJsonAsync(
            "/api/scenarios/people/flow/run",
            new ScenarioFlowRunRequest { Message = "" });
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
