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

public class GraphitiProviderAdapterTests
{
    [Fact]
    public async Task GetHealthAsync_parses_healthy_response()
    {
        var handler = new StubHttpHandler((req, _) =>
        {
            req.RequestUri!.AbsolutePath.Should().Be("/healthcheck");
            return JsonResponse("""{"status":"healthy"}""");
        });

        var adapter = CreateAdapter(handler, "http://127.0.0.1:8001");
        var health = await adapter.GetHealthAsync();
        health.Status.Should().Be(RagHealthStatus.Healthy);
    }

    [Fact]
    public async Task GetHealthAsync_connection_refused_returns_unavailable()
    {
        var handler = new ThrowingHttpHandler(new HttpRequestException("Connection refused (127.0.0.1:8001)"));
        var adapter = CreateAdapter(handler, "http://127.0.0.1:8001");
        var health = await adapter.GetHealthAsync();
        health.Status.Should().Be(RagHealthStatus.Unavailable);
        health.Message.Should().Contain("not reachable");
    }

    [Fact]
    public async Task QueryAsync_maps_facts_from_search()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var handler = new StubHttpHandler((req, reqBody) =>
        {
            captured = req;
            body = reqBody;
            req.RequestUri!.AbsolutePath.Should().Be("/search");
            req.Method.Should().Be(HttpMethod.Post);
            return JsonResponse("""
                {
                  "facts":[
                    {"uuid":"f1","name":"plays_sport","fact":"Ryan plays soccer.","created_at":"2026-01-01T00:00:00Z"}
                  ]
                }
                """);
        });

        var adapter = CreateAdapter(handler, "http://127.0.0.1:8001");
        var result = await adapter.QueryAsync(new RagQueryRequest(
            "Who is Ryan?",
            CollectionId: "person_1",
            Mode: RagQueryMode.Graph));

        result.Chunks.Should().ContainSingle(c => c.Text.Contains("soccer"));
        body.Should().Contain("person_1");
        body.Should().Contain("max_facts");
        captured!.RequestUri!.AbsolutePath.Should().Be("/search");
    }

    [Fact]
    public async Task IngestAsync_posts_messages()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var handler = new StubHttpHandler((req, reqBody) =>
        {
            captured = req;
            body = reqBody;
            return new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = new StringContent(
                    """{"message":"Messages added to processing queue","success":true}""",
                    Encoding.UTF8,
                    "application/json")
            };
        });

        var adapter = CreateAdapter(handler, "http://127.0.0.1:8001");
        var result = await adapter.IngestAsync(new RagIngestRequest(
            "scenarios/person_1/people/ryan/profile.md",
            Content: "Hello world",
            CollectionId: "person_1"));

        result.Success.Should().BeTrue();
        captured!.RequestUri!.AbsolutePath.Should().Be("/messages");
        body.Should().Contain("group_id");
        body.Should().Contain("person_1");
        body.Should().Contain("scenarios__person_1__people__ryan__profile.md");
        body.Should().Contain("Hello world");
    }

    [Fact]
    public async Task QueryAsync_uses_default_group_when_collection_blank()
    {
        string? body = null;
        var handler = new StubHttpHandler((_, reqBody) =>
        {
            body = reqBody;
            return JsonResponse("""{"facts":[]}""");
        });

        var adapter = CreateAdapter(handler, "http://127.0.0.1:8001", defaultGroupId: "team-alpha");
        await adapter.QueryAsync(new RagQueryRequest("ping", CollectionId: null));
        body.Should().Contain("team-alpha");
    }

    private static GraphitiProviderAdapter CreateAdapter(
        HttpMessageHandler handler,
        string baseUrl,
        string defaultGroupId = "agctor")
    {
        var http = new HttpClient(handler);
        var rest = new RestRagTransport(http);
        var options = new TestOptionsMonitor(new RagOptions
        {
            Graphiti = new GraphitiProviderOptions
            {
                BaseUrl = baseUrl,
                DefaultGroupId = defaultGroupId
            }
        });
        return new GraphitiProviderAdapter(options, rest);
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
