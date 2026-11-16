using System.Net;
using System.Text.Json;
using AgctorSDK.Host.Services.VideoWorkflow;
using Xunit;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class SceneVideoCriticTests
{
    [Theory]
    [InlineData("passed", "[]", "uncertain")]
    [InlineData("passed", "[\"Unexpected sword\"]", "uncertain")]
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
    [Theory]
    [InlineData("passed", "[]", "passed")]
    [InlineData("concerns", "[\"Repeated entrance\"]", "concerns")]
    [InlineData("concerns", "[\"Unexplained reversal\"]", "concerns")]
    [InlineData("uncertain", "[]", "uncertain")]
    [InlineData("not_applicable", "[]", "uncertain")]
    public async Task Ordered_boundaries_check_action_progression_even_when_appearance_matches(string transition, string concerns, string expected)
    {
        var handler = new Handler(async request => {
            var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync()).RootElement;
            var messages = body.GetProperty("messages");
            Assert.Contains("repeated entrances", messages[0].GetProperty("content").GetString());
            Assert.Contains("Distinguish camera changes", messages[0].GetProperty("content").GetString());
            Assert.Equal(new[] { "p1", "p2", "p3", "o1", "o2", "o3", "middle", "e1", "e2", "e3" },
                messages[1].GetProperty("images").EnumerateArray().Select(x => x.GetString()));
            var ending = new { status = "observed", position = "Inside", motion = "Toward bench", heldObjects = "Pot", completedActions = "Entrance complete", camera = "Behind" };
            return new(HttpStatusCode.OK) { Content = JsonContent(new { status = "passed", summary = "Sampled movement reviewed", concerns = JsonSerializer.Deserialize<string[]>(concerns), transitionStatus = transition, ending }) };
        });
        var current = new SceneVideoEvidence(["start", "middle", "end"], PreviousExpected: true,
            Opening: new(["o1", "o2", "o3"], [0, .75, 1.5]), Ending: new(["e1", "e2", "e3"], [13.5, 14.25, 14.95]),
            PreviousEnding: new(["p1", "p2", "p3"], [13.5, 14.25, 14.95]));
        var result = await new OllamaSceneVideoCritic(new Factory(handler)).CheckAsync("Continue watering", current, null, default);
        Assert.Equal(expected, result.Status); Assert.Equal("Entrance complete", result.Ending!.CompletedActions);
        Assert.False(string.IsNullOrWhiteSpace(result.Model));
    }
    [Fact]
    public async Task Invalid_sample_timing_cannot_claim_a_verified_transition()
    {
        var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
            Content = JsonContent(new { status = "passed", summary = "Looks consistent", concerns = Array.Empty<string>(), transitionStatus = "passed" }) }));
        var result = await new OllamaSceneVideoCritic(new Factory(handler)).CheckAsync("Continue", new(["a", "b", "c"], "prior", true,
            Opening: new(["a", "b", "c"], [0, 0, 1]), Ending: new(["a", "b", "c"], [2, 3, 4])), null, default);
        Assert.Equal("uncertain", result.Status); Assert.Null(result.Ending);
    }
    private static StringContent JsonContent(object report) => new(JsonSerializer.Serialize(new { message = new { content = JsonSerializer.Serialize(report) } }));
    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory { public HttpClient CreateClient(string name) => new(handler, false); }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
