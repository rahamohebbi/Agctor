using System.Net.Http.Json;
using AgctorSDK.Host.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace AgctorSDK.Host.IntegrationTests
{
    /// <summary>
    /// PRD-011: SSE streaming endpoint returns text/event-stream and a terminal done event.
    /// </summary>
    public class AgentsMessageStreamIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;
        private static int _portCounter = 9200;

        public AgentsMessageStreamIntegrationTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    var uniquePort = Interlocked.Increment(ref _portCounter);
                    config.AddInMemoryCollection(new[]
                    {
                        new KeyValuePair<string, string?>("Mcp:Port", uniquePort.ToString())
                    });
                });
            });
        }

        [Fact]
        public async Task MessageStream_UnknownAgent_ReturnsSseWithDoneAndAgentNotFound()
        {
            var client = _factory.CreateClient();
            var req = new MessageRequest { Payload = "hello", SessionId = null };
            using var message = new HttpRequestMessage(HttpMethod.Post, "/api/agents/definitely-missing-agent-xyz/message/stream")
            {
                Content = JsonContent.Create(req)
            };
            message.Headers.TryAddWithoutValidation("Accept", "text/event-stream");

            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            Assert.Contains("text/event-stream", response.Content.Headers.ContentType?.MediaType ?? "", StringComparison.OrdinalIgnoreCase);

            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("data:", body, StringComparison.Ordinal);
            Assert.Contains("\"type\":\"phase\"", body, StringComparison.Ordinal);
            Assert.Contains("\"type\":\"done\"", body, StringComparison.Ordinal);
            Assert.Contains("AgentNotFound", body, StringComparison.OrdinalIgnoreCase);
        }
    }
}
