using System.Net;
using System.Net.Http.Json;
using AgctorSDK.CodeGraph.Persistence;
using AgctorSDK.Host.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace AgctorSDK.Host.IntegrationTests
{
    /// <summary>
    /// Verifies file preview still works after chat interactions that refresh actor tree data.
    /// </summary>
    public class CodeGraphFilePreviewAfterChatIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly HttpClient _client;
        private static int _portCounter = 13080;

        public CodeGraphFilePreviewAfterChatIntegrationTests(WebApplicationFactory<Program> factory)
        {
            var configured = factory.WithWebHostBuilder(builder =>
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
            _client = configured.CreateClient();
        }

        [Fact]
        public async Task FilePreviewEndpoint_RemainsUsable_AfterSessionChatFollowup()
        {
            var setup = await _client.PostAsJsonAsync("/api/test/setup-scenario", new ScenarioSetupRequest("code-graph-demo", new Dictionary<string, object>
            {
                ["useStubEmbeddings"] = true
            }));
            setup.StatusCode.Should().Be(HttpStatusCode.OK);

            var context = await _client.GetFromJsonAsync<CodeGraphContextDto>("/api/CodeGraph/current");
            context.Should().NotBeNull();
            context!.ActorTree.Should().NotBeNull();

            var filePath = FindFirstFilePath(context.ActorTree!);
            filePath.Should().NotBeNullOrWhiteSpace();

            var firstPreview = await _client.GetAsync($"/api/CodeGraph/file-content?path={Uri.EscapeDataString(filePath!)}");
            firstPreview.StatusCode.Should().Be(HttpStatusCode.OK);

            var sessionResp = await _client.PostAsJsonAsync("/api/chat/sessions", new CreateChatSessionRequest());
            sessionResp.StatusCode.Should().Be(HttpStatusCode.Created);
            var session = await sessionResp.Content.ReadFromJsonAsync<AgctorSDK.Core.Sessions.Models.SessionInfo>();
            session.Should().NotBeNull();

            var firstChat = await _client.PostAsJsonAsync("/api/agents/query-agent/message", new MessageRequest
            {
                Payload = "what does MathUtils do ?",
                SessionId = session!.SessionId
            });
            firstChat.StatusCode.Should().Be(HttpStatusCode.OK);

            var secondChat = await _client.PostAsJsonAsync("/api/agents/query-agent/message", new MessageRequest
            {
                Payload = "how many methods does MathUtils have?",
                SessionId = session.SessionId
            });
            secondChat.StatusCode.Should().Be(HttpStatusCode.OK);

            var refreshed = await _client.GetFromJsonAsync<CodeGraphContextDto>("/api/CodeGraph/current");
            refreshed.Should().NotBeNull();
            refreshed!.ActorTree.Should().NotBeNull();

            var secondPreview = await _client.GetAsync($"/api/CodeGraph/file-content?path={Uri.EscapeDataString(filePath)}");
            secondPreview.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await secondPreview.Content.ReadAsStringAsync();
            body.Should().Contain("namespace DemoApp");
        }

        private static string? FindFirstFilePath(ActorSerializer.ActorDto node)
        {
            if (!string.IsNullOrWhiteSpace(node.ActorType) &&
                node.ActorType.Contains("File", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(node.PhysicalPath))
            {
                return node.PhysicalPath;
            }

            if (node.Children == null || node.Children.Count == 0)
            {
                return null;
            }

            foreach (var child in node.Children)
            {
                var path = FindFirstFilePath(child);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    return path;
                }
            }

            return null;
        }
    }
}
