using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;
using FluentAssertions;

namespace AgctorSDK.Host.IntegrationTests;

/// <summary>
/// PRD-012: GET/PUT /api/runtime.
/// </summary>
public class RuntimeApiIntegrationTests : IClassFixture<AgctorWebApplicationFactory>
{
    private readonly HttpClient _client;
    private static int _portCounter = 14280;

    public RuntimeApiIntegrationTests(AgctorWebApplicationFactory factory)
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
    public async Task GetRuntime_ReturnsOk_WithCurrentAvailableConfigured()
    {
        var response = await _client.GetAsync("/api/runtime");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("current", out var cur).Should().BeTrue();
        cur.TryGetProperty("canonicalId", out _).Should().BeTrue();
        cur.TryGetProperty("adapterName", out _).Should().BeTrue();
        cur.TryGetProperty("version", out _).Should().BeTrue();
        cur.TryGetProperty("isInitialized", out _).Should().BeTrue();
        json.TryGetProperty("configured", out var cfg).Should().BeTrue();
        cfg.TryGetProperty("defaultRuntime", out _).Should().BeTrue();
        json.TryGetProperty("available", out var avail).Should().BeTrue();
        avail.ValueKind.Should().Be(JsonValueKind.Array);
        avail.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task PutRuntime_Unknown_Returns400()
    {
        var res = await _client.PutAsJsonAsync("/api/runtime", new { defaultRuntime = "NotARealRuntime" });
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PutRuntime_Valid_ReturnsRequiresRestart()
    {
        var res = await _client.PutAsJsonAsync("/api/runtime", new { defaultRuntime = "InMemory" });
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("requiresRestart", out var rr).Should().BeTrue();
        rr.GetBoolean().Should().BeFalse();
        body.TryGetProperty("persistedCanonicalRuntime", out var pr).Should().BeTrue();
        pr.GetString().Should().Be("InMemory");
    }

    [Fact]
    public async Task GetRuntimeHealth_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/runtime/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("liveRuntimeId", out _).Should().BeTrue();
        json.TryGetProperty("overallStatus", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GetRuntime_ReturnsMaturityOnAvailable()
    {
        var response = await _client.GetAsync("/api/runtime");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var first = json.GetProperty("available")[0];
        first.TryGetProperty("maturity", out _).Should().BeTrue();
        first.TryGetProperty("configFields", out _).Should().BeTrue();
    }

    [Fact]
    public async Task PutRuntime_ProtoAlias_NormalizesToProtoActor()
    {
        var res = await _client.PutAsJsonAsync("/api/runtime", new { defaultRuntime = "Proto", protoHost = "127.0.0.1", protoPort = 12000 });
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("persistedCanonicalRuntime").GetString().Should().Be("Proto.Actor");
    }

    [Fact]
    public async Task GetDockerStatus_Orleans_ReturnsStatusShape()
    {
        var response = await _client.GetAsync("/api/runtime/docker/Orleans");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("runtimeId", out var id).Should().BeTrue();
        id.GetString().Should().Be("Orleans");
        json.TryGetProperty("state", out _).Should().BeTrue();
        json.TryGetProperty("statusText", out _).Should().BeTrue();
        json.TryGetProperty("serviceName", out var svc).Should().BeTrue();
        svc.GetString().Should().Be("orleans-silo");
    }
}
