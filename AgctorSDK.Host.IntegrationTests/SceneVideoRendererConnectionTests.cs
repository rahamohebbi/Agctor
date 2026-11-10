using System.Net;
using System.Net.Http.Json;
using AgctorSDK.Host.Services.VideoWorkflow;
using Microsoft.Extensions.Options;
using Xunit;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class SceneVideoRendererConnectionTests
{
    [Fact]
    public async Task Renderer_needs_only_a_url_and_sends_no_account_credentials()
    {
        var handler = new Recorder();
        var renderer = new HttpSceneVideoRenderer(new Clients(handler), new Settings());
        Assert.True(renderer.IsConfigured);
        await renderer.EnsureAsync("run-1-1", new("run", "workspace", "shot", 1, 1, 15, "A traveller arrives home.", null), default);
        await renderer.GetAsync("run-1-1", default);
        Assert.Equal(new[] { HttpMethod.Put, HttpMethod.Get }, handler.Methods);
    }
    [Theory]
    [InlineData("{\"code\":\"story_not_found\",\"error\":\"private upstream detail\"}", false, "AgctorRendering:Owner")]
    [InlineData("", false, "rendering route")]
    [InlineData("<html>private upstream detail</html>", false, "rendering route")]
    [InlineData("{\"code\":42}", false, "rendering route")]
    [InlineData("", true, "render job was not found")]
    public async Task Missing_story_or_route_asks_for_action_without_exposing_upstream_details(string body, bool polling, string expected)
    {
        var renderer = new HttpSceneVideoRenderer(new Clients(new MissingResource(body)), new Settings());
        var error = await Assert.ThrowsAsync<SceneRenderInterventionException>(() => polling
            ? renderer.GetAsync("run-1-1", default)
            : renderer.EnsureAsync("run-1-1", new("run", "workspace", "shot", 1, 1, 15, "A traveller arrives home.", null), default));
        Assert.Contains(expected, error.Message);
        Assert.Contains("Cynelo is reachable", error.Message);
        Assert.DoesNotContain("private upstream detail", error.Message);
    }
    private sealed class MissingResource(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent(body) });
    }
    [Theory]
    [InlineData("recovery_uncertain", "queue and history")]
    [InlineData("recovery_cancelled", "will not be resumed")]
    [InlineData("recovery_not_found", "configured Cynelo studio")]
    public async Task Recovery_conflicts_explain_the_safe_next_action(string code, string expected)
    {
        var renderer = new HttpSceneVideoRenderer(new Clients(new RecoveryConflict(code)), new Settings());
        var error = await Assert.ThrowsAsync<SceneRenderInterventionException>(() => renderer.GetAsync("run-1-1", default));
        Assert.Contains(expected, error.Message);
        Assert.DoesNotContain("private upstream detail", error.Message);
    }
    private sealed class RecoveryConflict(string code) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict) { Content = JsonContent.Create(new { code, error = "private upstream detail" }) });
    }
    [Fact]
    public async Task Comfy_outage_is_distinct_from_missing_job_and_does_not_expose_upstream_details()
    {
        var renderer=new HttpSceneVideoRenderer(new Clients(new Unavailable()),new Settings());
        var error=await Assert.ThrowsAsync<SceneRenderInterventionException>(()=>renderer.GetAsync("run-1-1",default));
        Assert.Equal("comfyui_unavailable",error.Code);
        Assert.Contains("Start ComfyUI",error.Message); Assert.Contains("does not submit another video",error.Message);
        Assert.DoesNotContain("private",error.Message);
    }
    private sealed class Unavailable : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) {Content=JsonContent.Create(new {code="comfyui_unavailable",error="private upstream details"})});
    }
    private sealed class Recorder : HttpMessageHandler
    {
        public List<HttpMethod> Methods { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Methods.Add(request.Method);
            Assert.Null(request.Headers.Authorization);
            Assert.Equal("http://127.0.0.1:5080/api/agctor/render/jobs/run-1-1", request.RequestUri!.AbsoluteUri);
            Assert.Equal("run-1-1", Assert.Single(request.Headers.GetValues("Idempotency-Key")));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new SceneRenderJob("job", "running")) });
        }
    }
    private sealed class Clients(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
    private sealed class Settings : IOptionsMonitor<SceneVideoOptions>
    {
        public SceneVideoOptions CurrentValue { get; } = new();
        public SceneVideoOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<SceneVideoOptions, string?> listener) => null;
    }
}
