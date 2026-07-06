using System.Net;
using System.Text;
using AgctorSDK.Extensions.Rag.Transport;
using FluentAssertions;
using Xunit;

namespace AgctorSDK.Core.Tests.Rag;

/// <summary>Captures outbound HTTP for adapter tests.</summary>
internal sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string, HttpResponseMessage> _respond;

    public StubHttpHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond)
    {
        _respond = respond;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content != null
            ? await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)
            : "";
        return _respond(request, body);
    }
}

public class McpHttpRagTransportTests
{
    [Fact]
    public async Task InvokeToolAsync_sends_tools_call_with_session()
    {
        string? posted = null;
        var call = 0;
        var handler = new StubHttpHandler((req, body) =>
        {
            call++;
            if (body.Contains("initialize", StringComparison.Ordinal))
            {
                var init = new HttpResponseMessage(HttpStatusCode.OK);
                init.Headers.TryAddWithoutValidation("Mcp-Session-Id", "sess-1");
                init.Content = new StringContent(
                    "event: message\ndata: {\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"protocolVersion\":\"2024-11-05\"}}\n\n",
                    Encoding.UTF8,
                    "text/event-stream");
                return init;
            }

            posted = body;
            req.Headers.TryGetValues("Mcp-Session-Id", out var sessions).Should().BeTrue();
            sessions!.Should().Contain("sess-1");
            req.Headers.Accept.ToString().Should().Contain("text/event-stream");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    event: message
                    data: {"jsonrpc":"2.0","id":2,"result":{"content":[{"type":"text","text":"ok"}]}}
                    """,
                    Encoding.UTF8,
                    "text/event-stream")
            };
        });

        var transport = new McpHttpRagTransport(new HttpClient(handler));
        var result = await transport.InvokeToolAsync(
            "http://127.0.0.1:8000/mcp",
            "recall",
            new Dictionary<string, object?> { ["query"] = "hi" });

        result.Success.Should().BeTrue();
        call.Should().BeGreaterThanOrEqualTo(2);
        posted.Should().Contain("tools/call");
        posted.Should().Contain("recall");
    }
}
