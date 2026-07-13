using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgctorSDK.Core.Rag;
using AgctorSDK.Host.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace AgctorSDK.Host.IntegrationTests;

/// <summary>PRD-025 Phase 3: RAG Docker API + settings.</summary>
public class RagProviderDockerApiIntegrationTests : IClassFixture<AgctorWebApplicationFactory>
{
    private readonly HttpClient _client;
    private static int _portCounter = 15280;

    public RagProviderDockerApiIntegrationTests(AgctorWebApplicationFactory factory)
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
    public async Task GetDockerStatus_LightRag_ReturnsStatusShape()
    {
        var response = await _client.GetAsync("/api/rag-providers/docker/LightRAG");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("providerId").GetString().Should().Be(RagProviderIds.LightRag);
        json.GetProperty("serviceName").GetString().Should().Be("lightrag");
        json.TryGetProperty("state", out _).Should().BeTrue();
        json.TryGetProperty("composeFileFound", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GetDockerStatus_Cognee_ReturnsStatusShape()
    {
        var response = await _client.GetAsync("/api/rag-providers/docker/Cognee");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("providerId").GetString().Should().Be(RagProviderIds.Cognee);
        json.GetProperty("serviceName").GetString().Should().Be("cognee-mcp");
    }

    [Fact]
    public async Task GetDockerStatus_Graphiti_ReturnsStatusShape()
    {
        var response = await _client.GetAsync("/api/rag-providers/docker/Graphiti");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("providerId").GetString().Should().Be(RagProviderIds.Graphiti);
        json.GetProperty("serviceName").GetString().Should().Be("graphiti");
        json.TryGetProperty("state", out _).Should().BeTrue();
        json.TryGetProperty("composeFileFound", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GetDockerStatus_None_ReturnsNotApplicable()
    {
        var response = await _client.GetAsync("/api/rag-providers/docker/None");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("state").GetString().Should().Be("not_applicable");
    }

    [Fact]
    public void RagProviderConfigBuilder_binds_agctor_rag_section()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agctor:Rag:DefaultProvider"] = "Graphiti",
                ["Agctor:Rag:LightRAG:BaseUrl"] = "http://localhost:9999",
                ["Agctor:Rag:Graphiti:BaseUrl"] = "http://localhost:8001",
                ["Agctor:Rag:Graphiti:DefaultGroupId"] = "demo",
                ["Agctor:Rag:Cognee:SearchType"] = "GRAPH_COMPLETION"
            })
            .Build();

        var options = RagProviderConfigBuilder.FromConfiguration(config);
        options.DefaultProvider.Should().Be(RagProviderIds.Graphiti);
        options.LightRAG.BaseUrl.Should().Be("http://localhost:9999");
        options.Graphiti.BaseUrl.Should().Be("http://localhost:8001");
        options.Graphiti.DefaultGroupId.Should().Be("demo");
        options.Cognee.SearchType.Should().Be("GRAPH_COMPLETION");
    }

    [Fact]
    public async Task UserRagSettingsService_persists_default_provider()
    {
        var temp = Path.Combine(Path.GetTempPath(), "agctor-rag-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var env = new TestHostEnvironment(temp);

        try
        {
            var svc = new UserRagSettingsService(env, Microsoft.Extensions.Logging.Abstractions.NullLogger<UserRagSettingsService>.Instance);
            await svc.PersistAsync(new RagSettingsUpdate
            {
                DefaultProvider = RagProviderIds.Cognee,
                Cognee = new CogneeProviderOptions { BaseUrl = "http://127.0.0.1:8000" }
            });

            var path = Path.Combine(temp, "appsettings.User.json");
            File.Exists(path).Should().BeTrue();
            var text = await File.ReadAllTextAsync(path);
            text.Should().Contain("Cognee");
            text.Should().Contain("DefaultProvider");
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string contentRoot) => ContentRootPath = contentRoot;
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "AgctorSDK.Host.Tests";
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
