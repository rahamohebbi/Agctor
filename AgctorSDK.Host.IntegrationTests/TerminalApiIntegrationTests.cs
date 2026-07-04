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
