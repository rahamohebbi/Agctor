using AgctorSDK.Core.Rag;
using FluentAssertions;
using Xunit;

namespace AgctorSDK.Core.Tests.Rag;

/// <summary>The shared catalog distinguishes document retrieval adapters from independent Scene Memory.</summary>
public class RagProviderCatalogTests
{
    public static readonly string[] FactoryIds =
    {
        RagProviderIds.None, RagProviderIds.LightRag, RagProviderIds.Graphiti, RagProviderIds.Cognee
    };

    [Fact]
    public void Default_capable_entries_match_factory_ids()
    {
        var ids = RagProviderCatalog.All.Where(a => a.CanBeDefaultProvider).Select(a => a.Id).OrderBy(s => s).ToArray();
        ids.Should().Equal(FactoryIds.OrderBy(s => s).ToArray());
    }

    [Fact]
    public void Scene_memory_has_distinct_semantics_and_no_document_ingest_or_docker_configuration()
    {
        var scene = RagProviderCatalog.GetById(RagProviderIds.SceneMemory)!;
        scene.Kind.Should().Be(RagProviderKind.SceneMemory);
        scene.CanBeDefaultProvider.Should().BeFalse();
        scene.RequiresDocker.Should().BeFalse();
        scene.ContextStrategies.Should().BeEmpty();
        scene.Capabilities.Should().Contain("historical_queries");
        RagProviderConfigSchema.GetFields(scene.Id).Should().BeEmpty();
        RagProviderIds.IsKnown(scene.Id).Should().BeFalse();
        RagProviderCatalog.All.Where(p => FactoryIds.Contains(p.Id) && p.Id != RagProviderIds.None)
            .Should().OnlyContain(p => p.Kind == RagProviderKind.KnowledgeRag);
    }

    [Theory]
    [InlineData("None")]
    [InlineData("lightrag")]
    [InlineData("graphiti")]
    [InlineData("COGNEE")]
    [InlineData("scene-memory")]
    public void GetById_Is_Case_Insensitive(string key)
    {
        var d = RagProviderCatalog.GetById(key);
        d.Should().NotBeNull();
        d!.DisplayName.Should().NotBeNullOrWhiteSpace();
        d.Capabilities.Should().NotBeEmpty();
    }

    [Fact]
    public void GetById_Unknown_Returns_Null()
    {
        RagProviderCatalog.GetById("PageIndex").Should().BeNull();
    }

    [Theory]
    [InlineData(RagProviderIds.None, false)]
    [InlineData(RagProviderIds.LightRag, true)]
    [InlineData(RagProviderIds.Graphiti, true)]
    [InlineData(RagProviderIds.Cognee, true)]
    [InlineData(RagProviderIds.SceneMemory, false)]
    public void RequiresDocker_matches_provider(string id, bool expected)
    {
        RagProviderCatalog.GetById(id)!.RequiresDocker.Should().Be(expected);
    }

    [Theory]
    [InlineData("markdown_only", RagProviderIds.None)]
    [InlineData("Light-RAG", RagProviderIds.LightRag)]
    [InlineData("graphiti-rest", RagProviderIds.Graphiti)]
    public void Normalize_maps_aliases(string input, string expected)
    {
        RagProviderIds.Normalize(input).Should().Be(expected);
    }
}
