using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgctorSDK.Core.SceneMemory;
using AgctorSDK.Core.Rag;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AgctorSDK.Host.IntegrationTests;

// Host tool construction uses the existing static service accessor. Isolate this host from other test hosts.
[Collection("LlmStatic")]
public sealed class SceneMemoryIntegrationTests : IClassFixture<SceneMemoryWebApplicationFactory>
{
    private readonly HttpClient _client;
    public SceneMemoryIntegrationTests(SceneMemoryWebApplicationFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Host_can_record_query_and_create_a_real_agent_that_retrieves_the_same_history()
    {
        var created = await _client.PostAsJsonAsync("/api/scene-memory/workspaces", new { name = "Kitchen story" });
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        var workspace = (await created.Content.ReadFromJsonAsync<SceneWorkspace>())!;
        var path = $"/api/scene-memory/workspaces/{workspace.Id}";
        var entityResponse = await _client.PostAsJsonAsync(path + "/entities", new { name = "CupA", kind = "Object" });
        entityResponse.EnsureSuccessStatusCode();
        var entity = (await entityResponse.Content.ReadFromJsonAsync<SceneWorkspace>())!.Entities.Single();
        foreach (var value in new[] { "On kitchen table", "Held by ActorB" })
            (await _client.PostAsJsonAsync(path + "/facts", new { entityId = entity.Id, property = "location", value, note = "Manual test" })).EnsureSuccessStatusCode();
        var query = new SceneQuery(Question: "Where is CupA?", AtSequence: 1);
        var direct = await _client.PostAsJsonAsync(path + "/query", query);
        direct.EnsureSuccessStatusCode();
        var expected = (await direct.Content.ReadFromJsonAsync<SceneQueryResult>())!;
        Assert.Equal("On kitchen table", Assert.Single(expected.Facts).Value);

        var providerResponse = await _client.PostAsJsonAsync("/api/scene-memory/query",
            new SceneMemoryQueryRequest(workspace.Id, "Where is CupA?", AtSequence: 1));
        providerResponse.EnsureSuccessStatusCode();
        var providerJson = await providerResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(providerJson.GetProperty("success").GetBoolean());
        Assert.Equal(RagProviderIds.SceneMemory, providerJson.GetProperty("providerId").GetString());
        Assert.Equal(expected.Answer, providerJson.GetProperty("message").GetString());
        var providerResult = providerJson.GetProperty("result").Deserialize<SceneQueryResult>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(expected.Facts.ToArray(), providerResult.Facts.ToArray());
        Assert.Equal(expected.AtSequence, providerResult.AtSequence);

        var createAgent = await _client.PostAsJsonAsync(path + "/agents", new { name = "Continuity test" });
        createAgent.EnsureSuccessStatusCode();
        var agentId = (await createAgent.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        var viaAgent = await _client.PostAsJsonAsync(path + $"/agents/{agentId}/query", query);
        var responseText = await viaAgent.Content.ReadAsStringAsync();
        Assert.True(viaAgent.IsSuccessStatusCode, responseText);
        var json = JsonDocument.Parse(responseText).RootElement;
        Assert.Equal("scene-memory-query", json.GetProperty("tool").GetString());
        var result = json.GetProperty("result").Deserialize<SceneQueryResult>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(expected.Answer, result.Answer);
        Assert.Equal(expected.Facts.ToArray(), result.Facts.ToArray());

        var other = await _client.PostAsJsonAsync("/api/scene-memory/workspaces", new { name = "Other story" });
        var otherId = (await other.Content.ReadFromJsonAsync<SceneWorkspace>())!.Id;
        var mismatch = await _client.PostAsJsonAsync($"/api/scene-memory/workspaces/{otherId}/agents/{agentId}/query", query);
        Assert.Equal(HttpStatusCode.NotFound, mismatch.StatusCode);
        Assert.Contains(agentId!, await _client.GetStringAsync(path + "/agents"));
    }

    [Fact]
    public async Task Provider_health_and_query_outcomes_follow_the_rag_shape()
    {
        var ragHealth = await _client.GetFromJsonAsync<JsonElement>("/api/rag-providers/health");
        var sceneHealth = await _client.GetFromJsonAsync<JsonElement>("/api/scene-memory/health");
        foreach (var field in new[] { "providerId", "overallStatus", "providerHealthStatus", "detail" })
        {
            Assert.True(ragHealth.TryGetProperty(field, out _));
            Assert.True(sceneHealth.TryGetProperty(field, out _));
        }
        Assert.Equal("healthy", sceneHealth.GetProperty("overallStatus").GetString());
        Assert.Equal(RagProviderIds.SceneMemory, sceneHealth.GetProperty("providerId").GetString());
        var invalid = await _client.PostAsJsonAsync("/api/scene-memory/query", new SceneMemoryQueryRequest("workspace"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var invalidJson = await invalid.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(invalidJson.GetProperty("success").GetBoolean());
        Assert.Equal(RagProviderIds.SceneMemory, invalidJson.GetProperty("providerId").GetString());
        Assert.True(invalidJson.TryGetProperty("message", out _));
        var missing = await _client.PostAsJsonAsync("/api/scene-memory/query", new SceneMemoryQueryRequest("missing", "CupA"));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.False((await missing.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Rag_catalog_classifies_scene_memory_and_links_to_its_existing_workspace()
    {
        var status = await _client.GetFromJsonAsync<JsonElement>("/api/rag-providers");
        var available = status.GetProperty("available").EnumerateArray().ToArray();
        var scene = Assert.Single(available.Where(p => p.GetProperty("id").GetString() == RagProviderIds.SceneMemory));
        Assert.Equal("SceneMemory", scene.GetProperty("kind").GetString());
        Assert.False(scene.GetProperty("canBeDefaultProvider").GetBoolean());
        Assert.False(scene.GetProperty("requiresDocker").GetBoolean());
        var lightRag = Assert.Single(available.Where(p => p.GetProperty("id").GetString() == RagProviderIds.LightRag));
        Assert.Equal("KnowledgeRag", lightRag.GetProperty("kind").GetString());
        Assert.True(lightRag.GetProperty("canBeDefaultProvider").GetBoolean());

        var page = await _client.GetStringAsync("/Dashboard/RagProviders?provider=scene-memory");
        Assert.Contains("data-selected-provider=\"SceneMemory\"", page);
        Assert.Contains("data-provider-kind=\"SceneMemory\"", page);
        Assert.Contains("Knowledge RAG", page);
        Assert.Contains("href=\"/Dashboard/SceneMemory\" id=\"rag-open-scene-memory\"", page);
        Assert.DoesNotContain("id=\"rag-save-form\"", page);
        Assert.DoesNotContain("id=\"rag-docker-panel\"", page);
        Assert.DoesNotContain("id=\"rag-panel-index\"", page);
        Assert.DoesNotContain("id=\"rag-test-query-form\"", page);

        var response = await _client.PutAsJsonAsync("/api/rag-providers", new { defaultProvider = "scene-memory" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("alongside", await response.Content.ReadAsStringAsync());
        var after = await _client.GetFromJsonAsync<JsonElement>("/api/rag-providers");
        Assert.Equal(status.GetProperty("current").GetProperty("providerId").GetString(),
            after.GetProperty("current").GetProperty("providerId").GetString());
    }

    [Fact]
    public async Task Dashboard_and_validation_are_available_without_models_or_video()
    {
        var page = await _client.GetStringAsync("/Dashboard/SceneMemory");
        Assert.Contains("Test memory directly", page);
        Assert.Contains("scene-memory.js", page);
        var invalid = await _client.PostAsJsonAsync("/api/scene-memory/workspaces", new { name = " " });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var missing = await _client.PostAsJsonAsync("/api/scene-memory/workspaces/missing/query", new SceneQuery(Question: "CupA"));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}

public sealed class SceneMemoryWebApplicationFactory : AgctorWebApplicationFactory
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "agctor-scene-host-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Agctor:SceneMemory:StorePath"] = Path.Combine(_directory, "scene.db"),
            ["Agctor:SessionStorePath"] = Path.Combine(_directory, "sessions.db"),
            ["Agctor:LLM:OllamaApiUrl"] = "http://127.0.0.1:1",
            ["Agctor:Rag:DefaultProvider"] = RagProviderIds.None,
            ["Mcp:Port"] = "0"
        }));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
