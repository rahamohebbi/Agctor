using AgctorSDK.Core.Rag.Ingest;
using AgctorSDK.Extensions.Rag.Ingest;
using FluentAssertions;
using Xunit;

namespace AgctorSDK.Core.Tests.Rag;

public class AgctorMarkdownIngestSourceTests
{
    [Fact]
    public async Task PreviewAsync_finds_markdown_in_sample_project()
    {
        var root = SampleProjectRoot();
        if (root == null) return;

        var source = new AgctorMarkdownIngestSource();
        var preview = await source.PreviewAsync(new RagIngestSourceContext(root));

        preview.DocumentCount.Should().BeGreaterThan(0);
        preview.SamplePaths.Should().NotBeEmpty();
        preview.Message.Should().Contain("markdown");
    }

    [Fact]
    public async Task EnumerateDocumentsAsync_yields_scenario_people_paths()
    {
        var root = SampleProjectRoot();
        if (root == null) return;

        var source = new AgctorMarkdownIngestSource();
        var docs = new List<RagIngestDocument>();
        await foreach (var doc in source.EnumerateDocumentsAsync(new RagIngestSourceContext(root)))
            docs.Add(doc);

        docs.Should().NotBeEmpty();
        docs.Should().Contain(d => d.RelativePath.Contains("people/", StringComparison.OrdinalIgnoreCase));
        docs.Should().Contain(d => d.CollectionId == "person_1" || d.CollectionId == "people");
    }

    private static string? SampleProjectRoot()
    {
        var root = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "samples", "people-project"));
        return Directory.Exists(Path.Combine(root, ".agctor")) ? root : null;
    }
}
