using AgctorSDK.Core.ProjectMemory.Visual;
using AgctorSDK.Host.Models;
using AgctorSDK.Host.Services.Visual;
using FluentAssertions;
using Xunit;

namespace AgctorSDK.Host.IntegrationTests;

/// <summary>PRD-023f: inbox rows link to source photo for playground thumbnails.</summary>
public sealed class GenericInboxVisualEnricherTests
{
    [Fact]
    public async Task Enrich_keeps_explicit_source_asset_and_fills_legacy_rows_by_entity()
    {
        var catalog = new VisualAssetCatalogStore();
        var enricher = new GenericInboxVisualEnricher(catalog);
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "samples", "people-project"));
        const string scenario = "person_3";

        var items = new List<GenericInboxPendingItemDto>
        {
            new()
            {
                ProposalId = "p1",
                EntityKey = "raha",
                SourceAssetId = "explicit-asset"
            },
            new()
            {
                ProposalId = "p2",
                EntityKey = "raha"
            }
        };

        await enricher.EnrichWithSourceAssetsAsync(root, scenario, items, CancellationToken.None);

        items[0].SourceAssetId.Should().Be("explicit-asset");
        items[1].SourceAssetId.Should().NotBeNullOrWhiteSpace();
    }
}
