using System.Net.Http.Json;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Loading;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Rag;
using AgctorSDK.Core.ProjectMemory.Tools;
using AgctorSDK.Core.Rag;
using AgctorSDK.Extensions.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgctorSDK.Host.IntegrationTests;

/// <summary>PRD-025 Phase 5: Project Memory rag / graph_rag context strategies.</summary>
public class PersonMemoryRagContextIntegrationTests : IClassFixture<AgctorWebApplicationFactory>
{
    private static int _portCounter = 15480;

    [Fact]
    public async Task BuildAppendix_rag_strategy_with_none_provider_falls_back_to_markdown()
    {
        var root = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "samples", "people-project"));
        if (!Directory.Exists(root))
            return;

        await using var sp = BuildServices(RagProviderIds.None);
        var rag = sp.GetRequiredService<RagContextService>();
        var loader = new ProjectLoader();
        var ctx = await loader.LoadAsync(root);
        var spec = ctx.AgentSpecs.First(a => a.Id == "person-query");
        var ops = new ProjectMemoryOperations(loader, new EntityRegistry());

        var appendix = await PersonMemoryMarkdownContextBuilder.BuildAppendixAsync(
            ops,
            spec,
            root,
            "person_1",
            "rag",
            "Who is Ryan?",
            CancellationToken.None,
            ragService: rag);

        appendix.Should().Contain("could not use external RAG");
        appendix.Should().Contain("ryan");
    }

    [Fact]
    public async Task Get_rag_providers_api_available_for_scenarios_hint()
    {
        var factory = new AgctorWebApplicationFactory();
        var client = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var uniquePort = Interlocked.Increment(ref _portCounter);
                config.AddInMemoryCollection(new[]
                {
                    new KeyValuePair<string, string?>("Mcp:Port", uniquePort.ToString())
                });
            });
        }).CreateClient();

        var response = await client.GetAsync("/api/rag-providers");
        response.IsSuccessStatusCode.Should().BeTrue();
        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        json.TryGetProperty("configured", out var configured).Should().BeTrue();
        configured.TryGetProperty("defaultProvider", out _).Should().BeTrue();
    }

    private static ServiceProvider BuildServices(string defaultProvider)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAgctorRagProviders(configureOptions: o => o.DefaultProvider = defaultProvider);
        return services.BuildServiceProvider();
    }
}
