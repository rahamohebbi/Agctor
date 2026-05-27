using AgctorSDK.Core.ProjectMemory.Privacy;
using AgctorSDK.Core.ProjectMemory.Visual;
using AgctorSDK.Core.ProjectMemory.Visual.Models;
using AgctorSDK.Core.ProjectMemory.Visual.Storage;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.ProjectMemory;

[TestClass]
public sealed class PrivacyMemoryServiceVisualPurgeTests
{
    private string _root = "";

    [TestInitialize]
    public void Init() => _root = Path.Combine(Path.GetTempPath(), "privacy-visual-" + Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public async Task ForgetPerson_Removes_Visual_Asset_Yaml_For_Entity()
    {
        var scenarioId = "person_3";
        var catalog = new VisualAssetCatalogStore();
        var record = new VisualAssetRecord
        {
            AssetId = "photo-1",
            ScenarioId = scenarioId,
            ProjectId = "test",
            State = VisualAssetStates.InboxPending,
            Storage = new VisualAssetStorageRef { Bucket = "b", Key = "k", ContentType = "image/jpeg", Bytes = 10 },
            Subjects = [new VisualAssetSubject { EntityKey = "ryan", Role = "primary" }]
        };
        await catalog.SaveAsync(_root, scenarioId, record);

        var yamlPath = VisualAssetPaths.AssetCatalogPath(_root, scenarioId, "photo-1");
        Assert.IsTrue(File.Exists(yamlPath));

        var blobs = new FileSystemBlobStore(Options.Create(new VisualStorageOptions { Provider = "file" }));
        var purge = new VisualPersonPrivacyPurge(catalog, blobs);
        var svc = new PrivacyMemoryService(purge);

        var ok = await svc.ForgetPersonAsync(_root, scenarioId, "ryan");
        Assert.IsTrue(ok);
        Assert.IsFalse(File.Exists(yamlPath));
    }

    [TestMethod]
    public async Task ExportScenarioPeopleZip_Includes_Visual_Assets()
    {
        var scenarioId = "s1";
        var workspace = Path.Combine(_root, "scenarios", scenarioId);
        var peopleDir = Path.Combine(workspace, "people", "ryan");
        Directory.CreateDirectory(peopleDir);
        File.WriteAllText(Path.Combine(peopleDir, "profile.md"), "# Ryan");

        var visualDir = Path.Combine(workspace, "visual", "assets");
        Directory.CreateDirectory(visualDir);
        File.WriteAllText(Path.Combine(visualDir, "photo-1.yaml"), "assetId: photo-1\n");

        var svc = new PrivacyMemoryService();
        await using var zip = await svc.ExportScenarioPeopleZipAsync(_root, scenarioId);
        using var archive = new System.IO.Compression.ZipArchive(zip, System.IO.Compression.ZipArchiveMode.Read);
        var names = archive.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
        CollectionAssert.Contains(names, "people/ryan/profile.md");
        CollectionAssert.Contains(names, "visual/assets/photo-1.yaml");
    }
}
