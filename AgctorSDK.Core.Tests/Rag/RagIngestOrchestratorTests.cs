using AgctorSDK.Core.Rag;
using AgctorSDK.Core.Rag.Ingest;
using AgctorSDK.Extensions.DependencyInjection;
using AgctorSDK.Extensions.Rag.Ingest;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgctorSDK.Core.Tests.Rag;

public class RagIngestOrchestratorTests
{
    [Fact]
    public async Task IngestAsync_cognee_batches_by_dataset()
    {
        var calls = new List<(string SourcePath, string? CollectionId, string Content)>();
        var adapter = new RecordingAdapter(RagProviderIds.Cognee, calls);
        var source = new FakeSource(new[]
        {
            new RagIngestDocument("scenarios/a/people/x/a.md", "alpha body", "a"),
            new RagIngestDocument("scenarios/a/people/x/b.md", "beta body", "a"),
            new RagIngestDocument("scenarios/b/people/y/c.md", "charlie body", "b")
        });

        var orchestrator = new RagIngestOrchestrator(
            new RagIngestSourceRegistry(new[] { source }),
            new SingleAdapterFactory(adapter));

        var result = await orchestrator.IngestAsync(
            RagProviderIds.Cognee,
            RagIngestSourceIds.AgctorMarkdown,
            new RagIngestSourceContext("/tmp"));

        result.Success.Should().BeTrue();
        calls.Should().HaveCount(2);
        calls.Should().Contain(c => c.CollectionId == "a" && c.Content.Contains("a.md") && c.Content.Contains("b.md"));
        calls.Should().Contain(c => c.CollectionId == "b" && c.Content.Contains("c.md"));
        result.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task IngestAsync_with_none_provider_returns_error()
    {
        await using var sp = BuildServices();
        var orchestrator = sp.GetRequiredService<RagIngestOrchestrator>();

        var result = await orchestrator.IngestAsync(
            RagProviderIds.None,
            RagIngestSourceIds.AgctorMarkdown,
            new RagIngestSourceContext("/tmp"));

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Markdown only");
    }

    [Fact]
    public void Ingest_unimplemented_source_throws()
    {
        var orchestrator = new RagIngestOrchestrator(
            new RagIngestSourceRegistry(Array.Empty<IRagIngestSource>()),
            new FakeFactory());

        var act = () => orchestrator.PreviewAsync(RagIngestSourceIds.PdfDocument, new RagIngestSourceContext("/tmp"));
        act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void Registry_lists_planned_sources()
    {
        var registry = new RagIngestSourceRegistry(new IRagIngestSource[] { new AgctorMarkdownIngestSource() });
        registry.ListCatalog().Should().Contain(s => s.Id == RagIngestSourceIds.PdfDocument && !s.IsImplemented);
        registry.TryGetImplemented(RagIngestSourceIds.AgctorMarkdown).Should().NotBeNull();
        registry.TryGetImplemented(RagIngestSourceIds.PdfDocument).Should().BeNull();
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAgctorRagProviders();
        return services.BuildServiceProvider();
    }

    private sealed class FakeFactory : IRagProviderAdapterFactory
    {
        public IEnumerable<string> GetAvailableProviders() => new[] { RagProviderIds.None };
        public IRagProviderAdapter CreateDefaultProvider() => throw new NotImplementedException();
        public IRagProviderAdapter CreateProvider(string providerId) => throw new NotImplementedException();
        public string GetDefaultProviderId() => RagProviderIds.None;
        public bool IsProviderAvailable(string providerId) => true;
    }

    private sealed class SingleAdapterFactory : IRagProviderAdapterFactory
    {
        private readonly IRagProviderAdapter _adapter;

        public SingleAdapterFactory(IRagProviderAdapter adapter) => _adapter = adapter;

        public IEnumerable<string> GetAvailableProviders() => new[] { _adapter.ProviderId };
        public IRagProviderAdapter CreateDefaultProvider() => _adapter;
        public IRagProviderAdapter CreateProvider(string providerId) => _adapter;
        public string GetDefaultProviderId() => _adapter.ProviderId;
        public bool IsProviderAvailable(string providerId) => true;
    }

    private sealed class RecordingAdapter : IRagProviderAdapter
    {
        private readonly List<(string SourcePath, string? CollectionId, string Content)> _calls;

        public RecordingAdapter(string providerId, List<(string SourcePath, string? CollectionId, string Content)> calls)
        {
            ProviderId = providerId;
            _calls = calls;
        }

        public string ProviderId { get; }

        public Task<RagHealthResult> GetHealthAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new RagHealthResult(RagHealthStatus.Healthy, "ok"));

        public Task<RagQueryResult> QueryAsync(RagQueryRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RagQueryResult(Array.Empty<RagContextChunk>()));

        public Task<RagIngestResult> IngestAsync(RagIngestRequest request, CancellationToken cancellationToken = default)
        {
            _calls.Add((request.SourcePath, request.CollectionId, request.Content ?? ""));
            return Task.FromResult(new RagIngestResult(true, "accepted"));
        }
    }

    private sealed class FakeSource : IRagIngestSource
    {
        private readonly IReadOnlyList<RagIngestDocument> _docs;

        public FakeSource(IReadOnlyList<RagIngestDocument> docs) => _docs = docs;

        public string SourceId => RagIngestSourceIds.AgctorMarkdown;

        public Task<RagIngestSourcePreview> PreviewAsync(RagIngestSourceContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RagIngestSourcePreview(_docs.Count, _docs.Select(d => d.RelativePath).ToList(), "ok"));

        public async IAsyncEnumerable<RagIngestDocument> EnumerateDocumentsAsync(
            RagIngestSourceContext context,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var doc in _docs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return doc;
                await Task.Yield();
            }
        }
    }
}
