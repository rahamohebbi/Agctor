using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgctorSDK.Host.Services;
using Moq;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class OllamaDecisionClientTests
{
    [Fact]
    public async Task Discovery_UsesCapabilities_NotModelNames()
    {
        var inspected = new ConcurrentBag<string>();
        var client = Create(async request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/tags")
                return Response(new { models = new[] { new { name = "my-router:custom" }, new { name = "tev1-impostor" } } });
            request.RequestUri.AbsolutePath.Should().Be("/api/show");
            var body = await request.Content!.ReadFromJsonAsync<JsonElement>();
            var model = body.GetProperty("model").GetString()!;
            inspected.Add(model);
            return Response(new { capabilities = new[] { model == "my-router:custom" ? "decision" : "completion" } });
        });
        (await client.ListModelsAsync(default)).Should().Equal("my-router:custom");
        inspected.Should().HaveCount(2);
    }

    [Fact]
    public async Task Transport_SendsTypedQuestionsToSystemOne()
    {
        var client = Create(async request =>
        {
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri!.AbsolutePath.Should().Be("/v1/systemone");
            var body = await request.Content!.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("model").GetString().Should().Be("my-decision-model");
            body.GetProperty("state").GetString().Should().Be("A synthetic request");
            body.TryGetProperty("prompt", out _).Should().BeFalse();
            body.GetProperty("questions").GetProperty("check").GetProperty("type").GetString().Should().Be("noul");
            return Response(new { answers = new { check = new { type = "noul", noul = 0.9 } } });
        });
        var answer = await client.DecideAsync("my-decision-model", "A synthetic request",
            new Dictionary<string, object> { ["check"] = new { type = "noul", instructions = "Is this a request?" } }, default);
        answer.GetProperty("answers").GetProperty("check").GetProperty("noul").GetDouble().Should().Be(0.9);
    }

    [Fact]
    public async Task UnsupportedEndpoint_ReportsActionableErrorWithoutEchoingState()
    {
        var client = Create(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        { Content = new StringContent("Sensitive state echoed by provider") }));
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.DecideAsync("test", "private", new Dictionary<string, object>(), default));
        ex.Message.Should().Contain("HTTP 404").And.Contain("/v1/systemone").And.NotContain("Sensitive");
    }

    private static OllamaDecisionClient Create(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(OllamaDecisionClient.HttpClientName)).Returns(() => new HttpClient(new Handler(respond)));
        return new OllamaDecisionClient(factory.Object);
    }

    private static HttpResponseMessage Response(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
}
