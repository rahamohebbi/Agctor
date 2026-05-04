using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace AgctorSDK.Host.IntegrationTests;

/// <summary>PRD-014 Phase 11: <c>GET /api/project-memory/agents</c> supplies YAML <c>name</c> for LlmNode picker labels.</summary>
public sealed class ProjectMemoryAgentsListIntegrationTests : IClassFixture<AgctorWebApplicationFactory>
{
    private readonly HttpClient _client;
    private static int _portCounter = 17310;

    public ProjectMemoryAgentsListIntegrationTests(AgctorWebApplicationFactory factory)
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
    public async Task ListAgents_ReturnsDisplayNames_ForDefaultSampleProject()
    {
        var res = await _client.GetAsync("/api/project-memory/agents");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().BeGreaterThan(0);
        var hasNamed = false;
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            if (el.TryGetProperty("id", out var id) && id.GetString() == "memory-curator"
                && el.TryGetProperty("name", out var name)
                && name.GetString()?.Contains("Curator", StringComparison.Ordinal) == true)
            {
                hasNamed = true;
                break;
            }
        }

        hasNamed.Should().BeTrue("memory-curator should include YAML name for flow modal labels");

        JsonElement? extractor = null;
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            if (el.TryGetProperty("id", out var idEl) && idEl.GetString() == "person-extractor")
            {
                extractor = el;
                break;
            }
        }

        extractor.Should().NotBeNull("sample project should include person-extractor");
        var ex = extractor!.Value;
        ex.TryGetProperty("inputType", out var inT).Should().BeTrue();
        inT.GetString().Should().NotBeNullOrWhiteSpace();
        ex.TryGetProperty("outputType", out var outT).Should().BeTrue();
        (outT.GetString() ?? "").Should().Contain("memory_intents", "person-extractor output contract");
        ex.TryGetProperty("toolsAllow", out var allow).Should().BeTrue();
        allow.ValueKind.Should().Be(JsonValueKind.Array);
        allow.GetArrayLength().Should().BeGreaterThan(0);
        ex.TryGetProperty("memoryRead", out var memR).Should().BeTrue();
        memR.ValueKind.Should().Be(JsonValueKind.Array);
    }
}
