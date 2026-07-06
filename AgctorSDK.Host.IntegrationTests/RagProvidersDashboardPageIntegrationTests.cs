using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgctorSDK.Core.Rag;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AgctorSDK.Host.IntegrationTests;

/// <summary>PRD-025 Phase 4: RAG providers dashboard page and API.</summary>
public class RagProvidersDashboardPageIntegrationTests : IClassFixture<AgctorWebApplicationFactory>
{
    private readonly HttpClient _client;
    private static int _portCounter = 15380;

    public RagProvidersDashboardPageIntegrationTests(AgctorWebApplicationFactory factory)
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
    public async Task Get_RagProvidersPage_Contains_Shell_And_Script()
    {
        var response = await _client.GetAsync("/Dashboard/RagProviders?provider=LightRAG");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("id=\"rag-providers-dashboard\"");
        html.Should().Contain("rag-providers-dashboard.js");
        html.Should().Contain("terminal-command-panel.js");
        html.Should().Contain("data-terminal-command-panel");
        html.Should().Contain("Select provider");
        html.Should().Contain("RAG providers");
    }

    [Fact]
    public async Task GetStatus_ReturnsCatalogShape()
    {
        var response = await _client.GetAsync("/api/rag-providers");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("current", out var current).Should().BeTrue();
        json.TryGetProperty("configured", out _).Should().BeTrue();
        json.TryGetProperty("available", out var available).Should().BeTrue();
        available.GetArrayLength().Should().BeGreaterThanOrEqualTo(3);
        current.GetProperty("providerId").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetHealth_ReturnsStatusShape()
    {
        var response = await _client.GetAsync("/api/rag-providers/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("overallStatus", out _).Should().BeTrue();
        json.TryGetProperty("providerId", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Query_NoneProvider_ReturnsEmptyChunks()
    {
        await _client.PutAsJsonAsync("/api/rag-providers", new
        {
            defaultProvider = RagProviderIds.None
        });

        var response = await _client.PostAsJsonAsync("/api/rag-providers/query", new
        {
            query = "What is Agctor?",
            topK = 3
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("providerId").GetString().Should().Be(RagProviderIds.None);
        json.GetProperty("chunks").GetArrayLength().Should().Be(0);
    }
}
