using System.Net;
using System.Collections.Generic;
using System.Threading;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;
using FluentAssertions;

namespace AgctorSDK.Host.IntegrationTests;

/// <summary>
/// Smoke test for PRD-012 Razor page and script reference.
/// </summary>
public class ActorRuntimeDashboardPageIntegrationTests : IClassFixture<AgctorWebApplicationFactory>
{
    private readonly HttpClient _client;
    private static int _portCounter = 14380;

    public ActorRuntimeDashboardPageIntegrationTests(AgctorWebApplicationFactory factory)
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
    public async Task Get_ActorRuntimePage_Contains_Shell_And_Script()
    {
        var response = await _client.GetAsync("/Dashboard/ActorRuntime");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("id=\"actor-runtime-content\"");
        html.Should().Contain("actor-runtime-page.js");
    }
}
