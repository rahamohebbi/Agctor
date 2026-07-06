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

public class LightRagProviderAdapterTests
{
    [Fact]
    public async Task GetHealthAsync_parses_healthy_response()
    {
        var handler = new StubHttpHandler((req, _) =>
        {
            req.RequestUri!.AbsolutePath.Should().Be("/health");
            return JsonResponse("""{"status":"healthy","api_version":"1.0"}""");
        });

        var adapter = CreateAdapter(handler, "http://127.0.0.1:9621");
        var health = await adapter.GetHealthAsync();
        health.Status.Should().Be(RagHealthStatus.Healthy);
    }

    [Fact]
    public async Task GetHealthAsync_connection_refused_returns_unavailable()
    {
        var handler = new ThrowingHttpHandler(new HttpRequestException("Connection refused (127.0.0.1:9621)"));
        var adapter = CreateAdapter(handler, "http://127.0.0.1:9621");
        var health = await adapter.GetHealthAsync();
        health.Status.Should().Be(RagHealthStatus.Unavailable);
        health.Message.Should().Contain("not reachable");
    }

    [Fact]
    public async Task QueryAsync_maps_chunks_from_query_data()
    {
        var handler = new StubHttpHandler((req, _) =>
        {
            req.RequestUri!.AbsolutePath.Should().Be("/query/data");
            req.Method.Should().Be(HttpMethod.Post);
            return JsonResponse("""
                {
                  "status":"success",
                  "data":{
                    "chunks":[
                      {"content":"Ryan plays soccer.","file_path":"people/ryan/profile.md","reference_id":"1"}
                    ]
                  }
                }
                """);
        });

        var adapter = CreateAdapter(handler, "http://127.0.0.1:9621");
        var result = await adapter.QueryAsync(new RagQueryRequest("Who is Ryan?", null, Mode: RagQueryMode.Hybrid));
        result.Chunks.Should().ContainSingle(c => c.Text.Contains("soccer"));
        result.Chunks[0].SourcePath.Should().Contain("ryan");
    }

    [Fact]
    public async Task IngestAsync_posts_documents_text()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHttpHandler((req, body) =>
        {
            captured = req;
            body.Should().Contain("scenarios__person_1__people__ryan__profile.md");
            return JsonResponse("""{"status":"success","document_id":"doc-1"}""");
        });

        var adapter = CreateAdapter(handler, "http://127.0.0.1:9621");
        var result = await adapter.IngestAsync(new RagIngestRequest(
            "scenarios/person_1/people/ryan/profile.md",
            Content: "Hello world"));
        result.Success.Should().BeTrue();
        captured!.RequestUri!.AbsolutePath.Should().Be("/documents/text");
    }

    [Fact]
    public async Task IngestAsync_treats_409_duplicate_as_success()
    {
        var handler = new StubHttpHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = new StringContent(
                    """{"detail":"Document storage already contains 'profile.md'"}""",
                    Encoding.UTF8,
                    "application/json")
            });

        var adapter = CreateAdapter(handler, "http://127.0.0.1:9621");
        var result = await adapter.IngestAsync(new RagIngestRequest(
            "scenarios/person_1/people/ryan/profile.md",
            Content: "Hello world"));
        result.Success.Should().BeTrue();
        result.Message.Should().Contain("duplicate skipped");
    }

    private static LightRagProviderAdapter CreateAdapter(HttpMessageHandler handler, string baseUrl)
    {
        var http = new HttpClient(handler);
        var rest = new RestRagTransport(http);
        var options = new TestOptionsMonitor(new RagOptions
        {
            LightRAG = new LightRagProviderOptions { BaseUrl = baseUrl }
        });
        return new LightRagProviderAdapter(options, rest);
    }

    private sealed class TestOptionsMonitor : IOptionsMonitor<RagOptions>
    {
        public TestOptionsMonitor(RagOptions value) => CurrentValue = value;
        public RagOptions CurrentValue { get; }
        public RagOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<RagOptions, string?> listener) => null;
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class ThrowingHttpHandler : HttpMessageHandler
    {
        private readonly Exception _exception;

        public ThrowingHttpHandler(Exception exception) => _exception = exception;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(_exception);
    }
}
