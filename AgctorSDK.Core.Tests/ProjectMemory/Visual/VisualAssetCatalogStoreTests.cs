using AgctorSDK.Core.ProjectMemory.Visual;
using AgctorSDK.Core.ProjectMemory.Visual.Models;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.ProjectMemory.Visual;

[TestClass]
public sealed class VisualAssetCatalogStoreTests
{
    [TestMethod]
    public async Task SaveAndLoad_round_trips_asset_yaml()
    {
        var root = Path.Combine(Path.GetTempPath(), "agctor-visual-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".agctor"));
        await File.WriteAllTextAsync(
            Path.Combine(root, ".agctor", "project.yaml"),
            "schemaVersion: 1\nprojectId: test-proj\nprojectType: people\n");

        var store = new VisualAssetCatalogStore();
        var record = new VisualAssetRecord
        {
            AssetId = "asset1",
            ScenarioId = "person_1",
            ProjectId = "test-proj",
            State = VisualAssetStates.PendingUpload,
            Storage = new VisualAssetStorageRef { Bucket = "b", Key = "k", ContentType = "image/jpeg", Bytes = 100 }
        };

        try
        {
            await store.SaveAsync(root, "person_1", record);
            var loaded = await store.LoadAsync(root, "person_1", "asset1");
            loaded.Should().NotBeNull();
            loaded!.AssetId.Should().Be("asset1");
            loaded.Storage.Bytes.Should().Be(100);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
