using System.Net;
using System.Net.Http.Json;
using AgctorSDK.Host.Models;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AgctorSDK.Host.IntegrationTests;

/// <summary>POST create + DELETE scenario against an isolated user catalog file (see <see cref="AgctorWebApplicationFactory"/>).</summary>
public sealed class ScenariosCatalogCrudIntegrationTests : IClassFixture<AgctorWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ScenariosCatalogCrudIntegrationTests(AgctorWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    [Fact]
    public async Task PostCreate_ThenDelete_RemovesUserOnlyScenario()
    {
        var id = "zz-itest-" + Guid.NewGuid().ToString("N")[..12];
        var post = await _client.PostAsJsonAsync(
            "/api/scenarios",
            new CreateScenarioRequest { Id = id, DisplayName = "ITest", Description = "temp" });
        post.StatusCode.Should().Be(HttpStatusCode.Created);

        await _client.PostAsync("/api/scenarios/reload", new StringContent(string.Empty));

        var list = await _client.GetFromJsonAsync<List<ScenarioDto>>("/api/scenarios");
        list.Should().NotBeNull();
        list!.Any(s => s.Id == id).Should().BeTrue();

        var del = await _client.DeleteAsync("/api/scenarios/" + Uri.EscapeDataString(id));
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await _client.PostAsync("/api/scenarios/reload", new StringContent(string.Empty));

        var after = await _client.GetFromJsonAsync<List<ScenarioDto>>("/api/scenarios");
        after.Should().NotBeNull();
        after!.Any(s => s.Id == id).Should().BeFalse();
    }
}
