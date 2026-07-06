using System.Net;
using System.Text;
using AgctorSDK.Core.Rag;
using AgctorSDK.Core.Rag.Transport;
using AgctorSDK.Extensions.Rag.Providers;
using AgctorSDK.Extensions.Rag.Transport;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AgctorSDK.Core.Tests.Rag;

public class CogneeProviderAdapterTests
{
    [Fact]
    public async Task GetHealthAsync_uses_tools_list()
    {
        var handler = new CogneeMcpStubHandler(_ =>
            SseJson("""
                {"jsonrpc":"2.0","id":2,"result":{"tools":[{"name":"recall"},{"name":"remember"}]}}
                """));

        var adapter = CreateAdapter(handler);
        var health = await adapter.GetHealthAsync();
        health.Status.Should().Be(RagHealthStatus.Healthy);
    }

    [Fact]
    public async Task QueryAsync_invokes_recall_tool()
    {
        var handler = new CogneeMcpStubHandler(body =>
        {
            body.Should().Contain("\"name\":\"recall\"");
            body.Should().Contain("GRAPH_COMPLETION");
            return SseJson("""
                {"jsonrpc":"2.0","id":2,"result":{"content":[{"type":"text","text":"Ryan is 12."}]}}
                """);
        });

        var adapter = CreateAdapter(handler);
        var result = await adapter.QueryAsync(new RagQueryRequest("Ryan age?", "people", Mode: RagQueryMode.Graph));
        result.Chunks.Should().ContainSingle(c => c.Text.Contains("Ryan"));
    }

    [Fact]
    public async Task IngestAsync_invokes_remember_tool()
    {
        var handler = new CogneeMcpStubHandler(body =>
        {
            body.Should().Contain("\"name\":\"remember\"");
            body.Should().Contain("dataset_name");
            return SseJson("""
                {"jsonrpc":"2.0","id":2,"result":{"content":[{"type":"text","text":"Indexing started."}]}}
                """);
        });

        var adapter = CreateAdapter(handler);
        var result = await adapter.IngestAsync(new RagIngestRequest("a.md", CollectionId: "people", Content: "fact"));
        result.Success.Should().BeTrue();
    }

    private static CogneeProviderAdapter CreateAdapter(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler);
        var mcp = new McpHttpRagTransport(http);
        var options = new TestOptionsMonitor(new RagOptions
        {
            Cognee = new CogneeProviderOptions
            {
                BaseUrl = "http://127.0.0.1:8000",
                McpPath = "/mcp",
                SearchType = "RAG_COMPLETION"
            }
        });
        return new CogneeProviderAdapter(options, mcp);
    }

    private sealed class TestOptionsMonitor : IOptionsMonitor<RagOptions>
    {
        public TestOptionsMonitor(RagOptions value) => CurrentValue = value;
        public RagOptions CurrentValue { get; }
        public RagOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<RagOptions, string?> listener) => null;
    }

    /// <summary>Simulates Cognee MCP initialize + session header before tool RPC.</summary>
    private sealed class CogneeMcpStubHandler : HttpMessageHandler
    {
        private readonly Func<string, HttpResponseMessage> _respondAfterInit;

        public CogneeMcpStubHandler(Func<string, HttpResponseMessage> respondAfterInit)
        {
            _respondAfterInit = respondAfterInit;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content != null
                ? await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)
                : "";

            if (body.Contains("initialize", StringComparison.Ordinal))
            {
                var init = new HttpResponseMessage(HttpStatusCode.OK);
                init.Headers.TryAddWithoutValidation("Mcp-Session-Id", "test-session");
                init.Content = new StringContent(
                    """
                    event: message
                    data: {"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2024-11-05","capabilities":{}}}
                    """,
                    Encoding.UTF8,
                    "text/event-stream");
                return init;
            }

            request.Headers.TryGetValues("Mcp-Session-Id", out var sessions).Should().BeTrue();
            sessions!.Should().Contain("test-session");
            return _respondAfterInit(body);
        }
    }

    private static HttpResponseMessage SseJson(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent($"event: message\ndata: {json}\n\n", Encoding.UTF8, "text/event-stream")
        };
}
