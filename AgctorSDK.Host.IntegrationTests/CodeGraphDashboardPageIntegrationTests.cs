using System.Net;
using System.Net.Http;
using System.Collections.Generic;
using System.Threading;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;
using FluentAssertions;

namespace AgctorSDK.Host.IntegrationTests;

/// <summary>
/// Verifies the CodeGraph Razor page composes PRD-007 ViewComponents and static dashboard script (smoke HTML).
/// </summary>
public class CodeGraphDashboardPageIntegrationTests : IClassFixture<AgctorWebApplicationFactory>
{
    private readonly HttpClient _client;
    private static int _portCounter = 14180;

    public CodeGraphDashboardPageIntegrationTests(AgctorWebApplicationFactory factory)
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
    public async Task Get_CodeGraphPage_Should_Contain_Prd007_ComponentMarkers_And_Script()
    {
        var response = await _client.GetAsync("/Dashboard/CodeGraph");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var html = await response.Content.ReadAsStringAsync();
        // Page shell + panel container
        html.Should().Contain("id=\"codegraph-content\"");
        html.Should().Contain("id=\"codegraph-panels\"");
        // Embedding store + chat + tree + debug + raw JSON (stable element ids from ViewComponents)
        html.Should().Contain("id=\"codegraph-vector-count\"");
        html.Should().Contain("id=\"codegraph-chat-send\"");
        html.Should().Contain("id=\"codegraph-actor-tree\"");
        html.Should().Contain("id=\"load-vectors-btn\"");
        html.Should().Contain("id=\"codegraph-raw-json\"");
        // Trace timeline component root (may be in hidden wrapper)
        html.Should().Contain("codegraph-trace-timeline");
        // Phase 4: external module (cache-busted src)
        html.Should().Contain("codegraph-page.js");
    }
}
