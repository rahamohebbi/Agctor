using System.Net;
using System.Net.Http.Json;
using AgctorSDK.Host.Services.VideoWorkflow;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Host.IntegrationTests;

public class SceneVideoStoryGatewayTests
{
    [Fact]
    public async Task Provisioning_replays_same_key_and_lists_studio_stories_without_credentials()
    {
        var handler = new Handler();
        var gateway = new HttpSceneVideoStoryGateway(new Clients(handler), new Settings());
        var first = await gateway.EnsureAsync("run-key", "Beach story", default);
        Assert.Equal(first, await gateway.EnsureAsync("run-key", "Beach story", default));
        Assert.Equal(first, Assert.Single(await gateway.ListAsync(default)));
        Assert.Equal(3, handler.Calls);
    }
    private sealed class Handler : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++; Assert.Null(request.Headers.Authorization);
            var story = new SceneVideoStory("workspace", "story", "Beach story");
            if (request.Method == HttpMethod.Put) {
                Assert.Equal("http://127.0.0.1:5080/api/agctor/render/stories/run-key", request.RequestUri!.AbsoluteUri);
                Assert.Equal("run-key", Assert.Single(request.Headers.GetValues("Idempotency-Key")));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(story) });
            }
            Assert.Equal("http://127.0.0.1:5080/api/agctor/render/stories", request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new[] { story }) });
        }
    }
    private sealed class Clients(HttpMessageHandler handler) : IHttpClientFactory {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
    private sealed class Settings : IOptionsMonitor<SceneVideoOptions> {
        public SceneVideoOptions CurrentValue { get; } = new();
        public SceneVideoOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<SceneVideoOptions, string?> listener) => null;
    }
}
