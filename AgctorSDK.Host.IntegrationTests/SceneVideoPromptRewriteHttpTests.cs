using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgctorSDK.Host.Models.SceneMemory;
using AgctorSDK.Host.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgctorSDK.Host.IntegrationTests;

[Collection("LlmStatic")]
public sealed class SceneVideoPromptRewriteHttpTests
{
    [Fact]
    public async Task Rewrite_returns_a_suggestion_without_changing_shot_state_and_rejects_stale_revision()
    {
        using var factory = new SceneStudioWebApplicationFactory();
        var handler = new Reply();
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddHttpClient(OllamaScenePromptInterpreter.ClientName).ConfigurePrimaryHttpMessageHandler(() => handler)));
        using var client = app.CreateClient();
        const string root = "/api/scene-memory/v1/workspaces";
        var connected = await client.PostAsJsonAsync(root + "/connect", new ConnectStoryRequest("Edit test", "cynelo", Guid.NewGuid().ToString("N"), "story"));
        connected.EnsureSuccessStatusCode();
        var story = (await connected.Content.ReadFromJsonAsync<SceneMemoryWorkspaceResponse>())!;
        var path = root + "/" + story.Id;
        (await client.PutAsJsonAsync(path + "/studio-entities/maya", new SyncStudioRequest("maya-design", 1, "Maya", "Character"))).EnsureSuccessStatusCode();
        var previewResponse = await client.PostAsJsonAsync(path + "/previews", new InterpretSceneRequest("Maya holds a mug.", Model: "test"));
        previewResponse.EnsureSuccessStatusCode();
        var preview = (await previewResponse.Content.ReadFromJsonAsync<SceneMemoryPreviewResponse>())!;
        (await client.PostAsync(path + "/previews/" + preview.Preview.Id + "/apply", null)).EnsureSuccessStatusCode();
        var before = await client.GetStringAsync(path + "/timeline");
        var timeline = JsonSerializer.Deserialize<SceneTimelineResponse>(before, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var endpoint = path + "/shots/" + Assert.Single(timeline.Shots).Id + "/video-prompt/rewrite";
        var input = new RewriteSceneVideoRequest(timeline.Revision, "Maya holds a mug.", "Make Maya younger", "test-chat");
        var response = await client.PostAsJsonAsync(endpoint, input);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal("Younger Maya holds a mug.", (await response.Content.ReadFromJsonAsync<RewriteVideoPromptResponse>())!.Prompt);
        Assert.Equal(before, await client.GetStringAsync(path + "/timeline"));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(endpoint, input with { ExpectedRevision = "stale" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(endpoint, input with { Prompt = "" })).StatusCode);
        Assert.Equal(1, handler.Calls);
    }

    private sealed class Reply : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new {
                message = new { content = "{\"prompt\":\"Younger Maya holds a mug.\"}" }
            }) });
        }
    }
}
