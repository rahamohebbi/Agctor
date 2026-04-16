using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgctorSDK.Host.Models;
using AgctorSDK.Host.Services.Scenarios;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using System.Threading;

namespace AgctorSDK.Host.IntegrationTests;

/// <summary>PRD-014: scenario <c>flow</c> GraphDocument round-trip via <c>PUT /api/scenarios</c>.</summary>
public sealed class ScenariosFlowIntegrationTests : IClassFixture<AgctorWebApplicationFactory>
{
    private readonly HttpClient _client;
    private static int _portCounter = 16280;

    public ScenariosFlowIntegrationTests(AgctorWebApplicationFactory factory)
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
    public async Task PutCatalog_RoundTripsFlow_ForCodeGraphDemo()
    {
        var list = await _client.GetFromJsonAsync<List<ScenarioDto>>("/api/scenarios");
        list.Should().NotBeNull();
        var catalog = list!;
        var target = catalog.FirstOrDefault(x => x.Id == "code-graph-demo");
        target.Should().NotBeNull();

        target!.Flow = new ScenarioFlowDocument
        {
            SchemaVersion = "1.0",
            GraphId = "code-graph-demo-flow",
            Name = "Test flow",
            Status = "active",
            OutputPolicy = "merge_sections",
            Nodes = new List<ScenarioFlowNode>
            {
                new() { Id = "in1", Type = "ChatInput", Label = "Chat", Config = null },
                new() { Id = "out1", Type = "Output", Label = "Out", Config = null }
            },
            Edges = new List<ScenarioFlowEdge>
            {
                new() { Id = "e1", FromNodeId = "in1", ToNodeId = "out1", Mode = "sequential" }
            }
        };

        var put = await _client.PutAsJsonAsync("/api/scenarios", new ScenarioCatalogUpdateRequest { Version = 1, Scenarios = catalog });
        put.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await _client.PostAsync("/api/scenarios/reload", new StringContent(string.Empty));

        var again = await _client.GetFromJsonAsync<List<ScenarioDto>>("/api/scenarios");
        var cg = again!.First(x => x.Id == "code-graph-demo");
        cg.Flow.Should().NotBeNull();
        cg.Flow!.GraphId.Should().Be("code-graph-demo-flow");
        cg.Flow.Nodes.Count.Should().BeGreaterThanOrEqualTo(2);
        cg.Flow.Edges.Should().Contain(e => e.Id == "e1" && e.FromNodeId == "in1" && e.ToNodeId == "out1");
    }

    [Fact]
    public async Task PutFlow_DedicatedEndpoint_PersistsWithoutFullCatalogPut()
    {
        var flow = new ScenarioFlowDocument
        {
            SchemaVersion = "1.0",
            GraphId = "code-graph-demo-flow-put-one",
            Name = "Dedicated PUT",
            Status = "active",
            OutputPolicy = "merge_sections",
            Nodes = new List<ScenarioFlowNode>
            {
                new() { Id = "in1", Type = "ChatInput", Label = "Chat", Config = null },
                new() { Id = "out1", Type = "Output", Label = "Out", Config = null }
            },
            Edges = new List<ScenarioFlowEdge>
            {
                new() { Id = "e1", FromNodeId = "in1", ToNodeId = "out1", Mode = "sequential" }
            }
        };

        var res = await _client.PutAsJsonAsync("/api/scenarios/code-graph-demo/flow", flow);
        res.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var again = await _client.GetFromJsonAsync<List<ScenarioDto>>("/api/scenarios");
        var cg = again!.First(x => x.Id == "code-graph-demo");
        cg.Flow.Should().NotBeNull();
        cg.Flow!.GraphId.Should().Be("code-graph-demo-flow-put-one");
        cg.Flow.Edges.Should().Contain(e => e.Id == "e1");
    }

    [Fact]
    public async Task GetScenarios_RawJsonUsesCamelCase_FlowSoBrowserFetchWorks()
    {
        var flow = new ScenarioFlowDocument
        {
            SchemaVersion = "1.0",
            GraphId = "camel-case-check-flow",
            Name = "Camel",
            Status = "active",
            OutputPolicy = "merge_sections",
            Nodes = new List<ScenarioFlowNode>
            {
                new() { Id = "in1", Type = "ChatInput", Label = "Chat", Config = null },
                new() { Id = "out1", Type = "Output", Label = "Out", Config = null }
            },
            Edges = new List<ScenarioFlowEdge>
            {
                new() { Id = "e1", FromNodeId = "in1", ToNodeId = "out1", Mode = "sequential" }
            }
        };

        (await _client.PutAsJsonAsync("/api/scenarios/code-graph-demo/flow", flow)).EnsureSuccessStatusCode();

        var raw = await _client.GetStringAsync("/api/scenarios");
        using var doc = JsonDocument.Parse(raw);
        var cg = doc.RootElement.EnumerateArray()
            .First(e => string.Equals(e.GetProperty("id").GetString(), "code-graph-demo", StringComparison.OrdinalIgnoreCase));

        cg.TryGetProperty("flow", out var flowEl).Should().BeTrue("dashboard JS expects s.flow from fetch().json()");
        cg.TryGetProperty("Flow", out _).Should().BeFalse("PascalCase Flow leaves s.flow undefined in the browser");
        flowEl.ValueKind.Should().Be(JsonValueKind.Object);
        flowEl.GetProperty("graphId").GetString().Should().Be("camel-case-check-flow");
    }
}
