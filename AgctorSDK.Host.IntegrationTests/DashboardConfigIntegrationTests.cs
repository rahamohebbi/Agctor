using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace AgctorSDK.Host.IntegrationTests;

/// <summary>
/// Integration tests for dashboard config API (PRD-006).
/// </summary>
public class DashboardConfigIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private static int _portCounter = 14080;

    public DashboardConfigIntegrationTests(WebApplicationFactory<Program> factory)
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
    public async Task GetConfig_ReturnsOk_WithRuntimeLlmToolsScenarios()
    {
        var response = await _client.GetAsync("/api/Config");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("runtime", out var runtime).Should().BeTrue();
        runtime.TryGetProperty("name", out _).Should().BeTrue();
        json.TryGetProperty("llm", out var llm).Should().BeTrue();
        llm.TryGetProperty("ollamaApiUrl", out _).Should().BeTrue();
        llm.TryGetProperty("defaultModel", out _).Should().BeTrue();
        json.TryGetProperty("tools", out var tools).Should().BeTrue();
        tools.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Array);
        json.TryGetProperty("scenarios", out var scenarios).Should().BeTrue();
        scenarios.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Object);
    }
}
