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
                    new KeyValuePair<string, string?>("Mcp:Port", uniquePort.ToString()),
                    new KeyValuePair<string, string?>("Agctor:Rag:DefaultProvider", RagProviderIds.None)
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

    [Theory]
    [InlineData("LightRAG", "LightRAG")]
    [InlineData("graphiti", "Graphiti")]
    [InlineData("Cognee", "Cognee")]
    [InlineData("unknown-provider", "None")]
    public async Task BrowsingProvider_DoesNotChangeDefault_AndNormalizesSelection(string requested, string expected)
    {
        var response = await _client.GetAsync("/Dashboard/RagProviders?provider=" + requested);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("data-selected-provider=\"" + expected + "\"");
        html.Should().Contain("data-active-provider=\"None\"");
        html.Should().Contain("href=\"/Dashboard/RagProviders?provider=LightRAG\"");
        html.Should().Contain("id=\"rag-save-btn\"");
        if (expected != "None")
        {
            html.Should().Contain("id=\"rag-panel-index\"");
            html.Should().Contain("id=\"rag-panel-test\"");
            html.Should().Contain("Save &amp; use " + expected);
        }
        var status = await _client.GetFromJsonAsync<JsonElement>("/api/rag-providers");
        status.GetProperty("current").GetProperty("providerId").GetString().Should().Be(RagProviderIds.None);
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
        available.GetArrayLength().Should().BeGreaterThanOrEqualTo(4);
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
        var response = await _client.PostAsJsonAsync("/api/rag-providers/query", new
        {
            query = "What is Agctor?",
            topK = 3,
            providerId = RagProviderIds.None
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("providerId").GetString().Should().Be(RagProviderIds.None);
        json.GetProperty("chunks").GetArrayLength().Should().Be(0);
    }
}
