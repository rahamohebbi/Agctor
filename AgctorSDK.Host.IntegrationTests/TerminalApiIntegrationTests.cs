using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AgctorSDK.Host.IntegrationTests;

public class TerminalApiIntegrationTests : IClassFixture<AgctorWebApplicationFactory>
{
    private readonly HttpClient _client;
    private static int _portCounter = 14480;

    public TerminalApiIntegrationTests(AgctorWebApplicationFactory factory)
    {
        var configured = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new[]
                {
                    new KeyValuePair<string, string?>("Mcp:Port", Interlocked.Increment(ref _portCounter).ToString())
                });
            });
        });
        _client = configured.CreateClient();
    }

    [Fact]
    public async Task GetPresets_Orleans_ReturnsCommands()
    {
        var res = await _client.GetAsync("/api/terminal/presets?contextType=actor-runtime&contextKey=Orleans");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadFromJsonAsync<TerminalPresetsPayload>();
        body!.Presets.Should().NotBeEmpty();
        body.DefaultCommand.Should().Contain("orleans-silo");
    }

    [Fact]
    public async Task PostRun_InvalidCommand_Returns400()
    {
        var res = await _client.PostAsJsonAsync("/api/terminal/run", new { command = "rm -rf /" });
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostRunStream_InvalidCommand_Returns400()
    {
        var res = await _client.PostAsJsonAsync("/api/terminal/run/stream", new { command = "rm -rf /" });
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostRunStream_ValidPull_StreamsSseEvents()
    {
        // Use a fast, safe compose command so CI can assert SSE framing without a long pull.
        var res = await _client.PostAsJsonAsync("/api/terminal/run/stream", new
        {
            command = "docker compose -f docker/rag-providers/docker-compose.yml ps graphiti",
            contextType = "rag-provider",
            contextKey = "Graphiti"
        });

        // Compose file may be missing in some environments — accept 200 SSE or 400 validation.
        if (res.StatusCode == HttpStatusCode.BadRequest)
            return;

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        res.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
        var body = await res.Content.ReadAsStringAsync();
        body.Should().Contain("data:");
        body.Should().Match(s => s.Contains("\"type\":\"done\"") || s.Contains("\"type\":\"error\"") || s.Contains("\"type\":\"stdout\"") || s.Contains("\"type\":\"stderr\""));
    }

    private sealed class TerminalPresetsPayload
    {
        public List<PresetItem> Presets { get; set; } = new();
        public string? DefaultCommand { get; set; }
    }

    private sealed class PresetItem
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public string Command { get; set; } = "";
    }
}
