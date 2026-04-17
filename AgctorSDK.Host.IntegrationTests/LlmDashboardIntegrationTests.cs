using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgctorSDK.Core.Agents;
using AgctorSDK.Host.Models;
using AgctorSDK.Host.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace AgctorSDK.Host.IntegrationTests;

[Collection("LlmStatic")]
public class LlmDashboardIntegrationTests : IClassFixture<AgctorWebApplicationFactory>
{
    private readonly AgctorWebApplicationFactory _factory;

    public LlmDashboardIntegrationTests(AgctorWebApplicationFactory factory) => _factory = factory;

    private static HttpClient CreateClient(
        AgctorWebApplicationFactory factory,
        IReadOnlyList<OllamaModelListItem>? catalog = null,
        bool noopUserSettings = false)
    {
        var items = catalog ?? new[]
        {
            new OllamaModelListItem { Name = "model-a:latest" },
            new OllamaModelListItem { Name = "model-b:latest" }
        };

        return factory.WithWebHostBuilder(b =>
        {
            b.ConfigureTestServices(services =>
            {
                services.AddSingleton<IOllamaModelCatalog>(_ => new FakeOllamaCatalog(items));
                if (noopUserSettings)
                    services.AddSingleton<ILlmUserSettingsService>(_ => new NoOpLlmUserSettings());
            });
        }).CreateClient();
    }

    [Fact]
    public async Task GetModels_ReturnsOk_WithFakeCatalog()
    {
        var client = CreateClient(_factory);
        var response = await client.GetAsync("/api/Llm/models");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<LlmModelsResponse>();
        dto.Should().NotBeNull();
        dto!.Models.Should().HaveCount(2);
        dto.Models.Select(m => m.Name).Should().Contain(new[] { "model-a:latest", "model-b:latest" });
    }

    [Fact]
    public async Task PutDefaultModel_UpdatesConfigDto_And_RestoresStaticDefaults()
    {
        var prevUrl = LLMAgent.GetConfiguredOllamaApiUrl();
        var prevModel = LLMAgent.GetConfiguredDefaultModel();
        try
        {
            LLMAgent.ConfigureDefaults(prevUrl, "mistral");

            var client = CreateClient(_factory, noopUserSettings: true);
            var put = await client.PutAsJsonAsync("/api/Llm/default-model", new LlmDefaultModelRequest { Model = "model-b:latest" });
            put.StatusCode.Should().Be(HttpStatusCode.OK);
            var putBody = await put.Content.ReadFromJsonAsync<SetLlmDefaultModelResponse>();
            putBody.Should().NotBeNull();
            putBody!.Warning.Should().BeNull();

            LLMAgent.GetConfiguredDefaultModel().Should().Be("model-b:latest");

            var cfg = await client.GetFromJsonAsync<JsonElement>("/api/Config");
            cfg.GetProperty("llm").GetProperty("defaultModel").GetString().Should().Be("model-b:latest");
        }
        finally
        {
            LLMAgent.ConfigureDefaults(prevUrl, prevModel);
        }
    }

    [Fact]
    public async Task PutDefaultModel_WhenModelMissingFromCatalog_ReturnsWarning()
    {
        var prevUrl = LLMAgent.GetConfiguredOllamaApiUrl();
        var prevModel = LLMAgent.GetConfiguredDefaultModel();
        try
        {
            LLMAgent.ConfigureDefaults(prevUrl, "mistral");
            var catalog = new[] { new OllamaModelListItem { Name = "only-one:latest" } };
            var client = CreateClient(_factory, catalog, noopUserSettings: true);

            var put = await client.PutAsJsonAsync("/api/Llm/default-model", new LlmDefaultModelRequest { Model = "ghost:latest" });
            put.StatusCode.Should().Be(HttpStatusCode.OK);
            var putBody = await put.Content.ReadFromJsonAsync<SetLlmDefaultModelResponse>();
            putBody!.Warning.Should().NotBeNullOrWhiteSpace();

            LLMAgent.GetConfiguredDefaultModel().Should().Be("ghost:latest");
        }
        finally
        {
            LLMAgent.ConfigureDefaults(prevUrl, prevModel);
        }
    }

    private sealed class FakeOllamaCatalog : IOllamaModelCatalog
    {
        private readonly IReadOnlyList<OllamaModelListItem> _items;

        public FakeOllamaCatalog(IReadOnlyList<OllamaModelListItem> items) => _items = items;

        public Task<IReadOnlyList<OllamaModelListItem>> ListLocalModelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_items);
    }

    private sealed class NoOpLlmUserSettings : ILlmUserSettingsService
    {
        public Task PersistDefaultModelAsync(string model, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
