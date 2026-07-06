using System.Text.Json;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Loading;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Tools;
using FluentAssertions;
using Xunit;

namespace AgctorSDK.Core.Tests.ProjectMemory;

public sealed class PersonMemoryMarkdownContextBuilderTests
{
    [Fact]
    public void ExtractFocusQueryFromUserMessage_Parses_Quoted_Name()
    {
        PersonMemoryMarkdownContextBuilder.ExtractFocusQueryFromUserMessage("Who is \"Jane Doe\" here?")
            .Should().Be("Jane Doe");
    }

    [Fact]
    public void ParseStrategy_Defaults_When_Config_Null()
    {
        PersonMemoryMarkdownContextBuilder.ParseStrategy(null).Should().Be("markdown_all");
    }

    [Fact]
    public void ParseStrategy_Reads_ContextStrategy_From_Json()
    {
        var el = JsonDocument.Parse("""{"contextStrategy":"markdown_focus"}""").RootElement;
        PersonMemoryMarkdownContextBuilder.ParseStrategy(el).Should().Be("markdown_focus");
    }

    [Fact]
    public void ParseRagOptions_Reads_FlowNode_Config()
    {
        var el = JsonDocument.Parse("""
            {"contextStrategy":"rag","ragProviderId":"LightRAG","ragCollectionId":"people","ragTopK":5}
            """).RootElement;
        var opts = PersonMemoryMarkdownContextBuilder.ParseRagOptions(el);
        opts.ProviderId.Should().Be("LightRAG");
        opts.CollectionId.Should().Be("people");
        opts.TopK.Should().Be(5);
    }

    [Fact]
    public async Task BuildAppendixAsync_rag_without_service_falls_back_with_note()
    {
        var root = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "samples", "people-project"));
        if (!Directory.Exists(root))
            return;

        var loader = new ProjectLoader();
        var ctx = await loader.LoadAsync(root);
        var spec = ctx.AgentSpecs.First(a => a.Id == "person-query");
        var ops = new ProjectMemoryOperations(loader, new EntityRegistry());

        var appendix = await PersonMemoryMarkdownContextBuilder.BuildAppendixAsync(
            ops,
            spec,
            root,
            "person_1",
            "rag",
            "Who is Ryan?",
            CancellationToken.None,
            ragService: null);

        appendix.Should().Contain("could not use external RAG");
        appendix.Should().Contain("ryan");
    }
}
