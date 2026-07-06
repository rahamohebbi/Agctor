using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AgctorSDK.Host.IntegrationTests;

/// <summary>PRD-025: modular RAG ingest API.</summary>
public class RagProviderIngestApiIntegrationTests : IClassFixture<AgctorWebApplicationFactory>
{
    private readonly HttpClient _client;
    private static int _portCounter = 15580;

    public RagProviderIngestApiIntegrationTests(AgctorWebApplicationFactory factory)
    {
        var sampleRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "samples", "people-project"));

        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var uniquePort = Interlocked.Increment(ref _portCounter);
                config.AddInMemoryCollection(new[]
                {
                    new KeyValuePair<string, string?>("Mcp:Port", uniquePort.ToString()),
                    new KeyValuePair<string, string?>("Agctor:ProjectMemory:ProjectRoot", sampleRoot)
                });
            });
        }).CreateClient();
    }

    [Fact]
    public async Task GetIngestSources_lists_agctor_markdown_and_planned_sources()
    {
        var response = await _client.GetAsync("/api/rag-providers/ingest/sources");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("projectRootConfigured").GetBoolean().Should().BeTrue();
        var sources = json.GetProperty("sources");
        sources.GetArrayLength().Should().BeGreaterThanOrEqualTo(3);
        sources.EnumerateArray().Should().Contain(s =>
            s.GetProperty("id").GetString() == "agctor_markdown"
            && s.GetProperty("isImplemented").GetBoolean());
    }

    [Fact]
    public async Task PreviewIngest_agctor_markdown_returns_document_count()
    {
        var response = await _client.PostAsJsonAsync("/api/rag-providers/ingest/preview", new
        {
            sourceId = "agctor_markdown"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("documentCount").GetInt32().Should().BeGreaterThan(0);
        json.GetProperty("samplePaths").GetArrayLength().Should().BeGreaterThan(0);
    }
}
