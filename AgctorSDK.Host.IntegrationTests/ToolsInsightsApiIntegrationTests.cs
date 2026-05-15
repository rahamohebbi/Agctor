using System.Net;
using System.Net.Http.Json;
using System.Threading;
using AgctorSDK.Host.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;
using FluentAssertions;

namespace AgctorSDK.Host.IntegrationTests;

/// <summary>GET /api/tools/agent-associations for the Tools dashboard.</summary>
public class ToolsInsightsApiIntegrationTests : IClassFixture<AgctorWebApplicationFactory>
{
    private readonly HttpClient _client;
    private static int _portCounter = 15180;

    public ToolsInsightsApiIntegrationTests(AgctorWebApplicationFactory factory)
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
    public async Task Get_ToolAgentAssociations_ReturnsToolsAndAssociations()
    {
        var response = await _client.GetAsync("/api/tools/agent-associations");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<ToolAgentsInsightResponse>();
        dto.Should().NotBeNull();
        dto!.Tools.Should().NotBeEmpty();
        dto.Tools.Should().Contain(t => t.ClrTypeName == "CodeEditorTool" && !string.IsNullOrWhiteSpace(t.Description));
        dto.Tools.Should().Contain(t => t.Associations.Any(a => a.Kind == "csharp-agent-type" && a.AgentId == "LLMAgent"));
        dto.UnmappedYamlAllowTokens.Should().NotBeNull();
    }

    [Fact]
    public async Task Get_AgentDefinitionsToolUsage_ReturnsAgentsWithTools()
    {
        var response = await _client.GetAsync("/api/agents/definitions/tool-usage");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<AgentToolsInsightResponse>();
        dto.Should().NotBeNull();
        dto!.Agents.Should().NotBeEmpty();
        dto.Agents.Should()
            .Contain(a => a.Kind == "csharp-agent-type" && a.AgentId == "LLMAgent" && a.Tools.Any(t => t.ClrTypeName == "CodeEditorTool"));
    }

    [Fact]
    public async Task Get_DashboardToolsPage_ContainsScript()
    {
        var response = await _client.GetAsync("/Dashboard/Tools");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("tools-page.js");
        html.Should().Contain("tools-dashboard-root");
    }
}
