using AgctorSDK.Core.Rag;
using FluentAssertions;
using Xunit;

namespace AgctorSDK.Core.Tests.Rag;

/// <summary>PRD-025: catalog ids stay aligned with IRagProviderAdapterFactory keys.</summary>
public class RagProviderCatalogTests
{
    public static readonly string[] FactoryIds = { RagProviderIds.None, RagProviderIds.LightRag, RagProviderIds.Cognee };

    [Fact]
    public void All_Contains_Exactly_Factory_Ids()
    {
        var ids = RagProviderCatalog.All.Select(a => a.Id).OrderBy(s => s).ToArray();
        ids.Should().Equal(FactoryIds.OrderBy(s => s).ToArray());
    }

    [Theory]
    [InlineData("None")]
    [InlineData("lightrag")]
    [InlineData("COGNEE")]
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
    [InlineData(RagProviderIds.Cognee, true)]
    public void RequiresDocker_matches_provider(string id, bool expected)
    {
        RagProviderCatalog.GetById(id)!.RequiresDocker.Should().Be(expected);
    }

    [Theory]
    [InlineData("markdown_only", RagProviderIds.None)]
    [InlineData("Light-RAG", RagProviderIds.LightRag)]
    public void Normalize_maps_aliases(string input, string expected)
    {
        RagProviderIds.Normalize(input).Should().Be(expected);
    }
}
