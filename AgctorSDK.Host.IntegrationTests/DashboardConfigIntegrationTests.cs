using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AgctorSDK.Host.IntegrationTests;

/// <summary>
/// Integration tests for dashboard config API (PRD-006).
/// </summary>
public class DashboardConfigIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public DashboardConfigIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
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
