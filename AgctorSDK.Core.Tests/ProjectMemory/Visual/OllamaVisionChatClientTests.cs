using System.Net;
using System.Text;
using System.Text.Json;
using AgctorSDK.Core.Ollama;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.ProjectMemory.Visual;

[TestClass]
public sealed class OllamaVisionChatClientTests
{
    [TestMethod]
    public async Task ChatAsync_sends_think_false_and_uses_content()
    {
        string? capturedBody = null;
        var handler = new StubHandler(req =>
        {
            capturedBody = req.Content == null ? null : req.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var responseJson = JsonSerializer.Serialize(new
            {
                message = new { role = "assistant", content = "{\"ok\":true}" },
                done = true
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        });

        var client = new OllamaVisionChatClient(new HttpClient(handler), NullLogger<OllamaVisionChatClient>.Instance);
        OllamaRuntimeConfiguration.ConfigureVision("gemma4:31b", Array.Empty<string>(), 120);

        var result = await client.ChatAsync("sys", "user", new[] { "aGVsbG8=" }, numPredict: 64, CancellationToken.None);

        result.Success.Should().BeTrue();
        capturedBody.Should().NotBeNullOrEmpty();
        capturedBody.Should().Contain("\"think\":false");
        result.Content.Should().Contain("ok");
    }

    [TestMethod]
    public async Task ChatAsync_falls_back_to_thinking_when_content_empty()
    {
        var handler = new StubHandler(_ =>
        {
            var responseJson = JsonSerializer.Serialize(new
            {
                message = new
                {
                    role = "assistant",
                    content = "",
                    thinking = "{\"memoryIntents\":[]}"
                },
                done = true
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        });

        var client = new OllamaVisionChatClient(new HttpClient(handler), NullLogger<OllamaVisionChatClient>.Instance);
        OllamaRuntimeConfiguration.ConfigureVision("gemma4:31b", Array.Empty<string>(), 120);

        var result = await client.ChatAsync("sys", "user", new[] { "aGVsbG8=" }, numPredict: null, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Content.Should().Contain("memoryIntents");
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(_handler(request));
    }
}
