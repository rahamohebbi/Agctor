using System.Net;
using System.Text.Json;
using AgctorSDK.Host.Services.VideoWorkflow;
using Xunit;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class SceneVideoCriticTests
{
    [Theory]
    [InlineData("passed", "[]", "passed")]
    [InlineData("passed", "[\"Unexpected sword\"]", "concerns")]
    [InlineData("uncertain", "[]", "uncertain")]
    [InlineData("invented", "[]", "unavailable")]
    [InlineData("passed", "null", "unavailable")]
    public async Task Structured_visual_review_includes_extension_frame_and_validates_result(string status, string concerns, string expected)
    {
        var handler = new Handler(async request => {
            var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync()).RootElement;
            Assert.False(body.GetProperty("stream").GetBoolean());
            var message = body.GetProperty("messages")[1];
            Assert.Equal(new[] { "previous", "start", "middle", "end" }, message.GetProperty("images").EnumerateArray().Select(x => x.GetString()));
            Assert.Contains("Story intentions", message.GetProperty("content").GetString());
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { message = new {
                content = $"{{\"status\":\"{status}\",\"summary\":\"Sampled frames reviewed\",\"concerns\":{concerns}}}" } })) };
        });
        var critic = new OllamaSceneVideoCritic(new Factory(handler));
        var result = await critic.CheckAsync("A woman on a beach", new(["start", "middle", "end"], "previous"), null, default);
        Assert.Equal(expected, result.Status);
    }
    [Fact]
    public async Task Missing_preceding_frame_cannot_pass_an_extension_review()
    {
        var critic = new OllamaSceneVideoCritic(new Factory(new Handler(_ => throw new InvalidOperationException("Should not call the model"))));
        var result = await critic.CheckAsync("Continue the story", new(["a", "b", "c"], PreviousExpected: true), null, default);
        Assert.Equal("unavailable", result.Status);
    }
    [Fact]
    public async Task Vision_outage_preserves_uncertainty_instead_of_approving()
    {
        var critic = new OllamaSceneVideoCritic(new Factory(new Handler(_ => throw new HttpRequestException("private provider details"))));
        var result = await critic.CheckAsync("Story", new(["a", "b", "c"]), null, default);
        Assert.Equal("unavailable", result.Status); Assert.DoesNotContain("private", result.Summary);
    }
    [Fact]
    public async Task Lease_cancellation_propagates_without_a_false_review_result()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        var critic = new OllamaSceneVideoCritic(new Factory(new Handler(_ => throw new OperationCanceledException(cts.Token))));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => critic.CheckAsync("Story", new(["a", "b", "c"]), null, cts.Token));
    }
    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory { public HttpClient CreateClient(string name) => new(handler, false); }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
