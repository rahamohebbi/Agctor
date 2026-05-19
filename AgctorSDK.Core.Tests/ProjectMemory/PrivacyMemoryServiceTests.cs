using AgctorSDK.Core.ProjectMemory.Privacy;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.ProjectMemory;

[TestClass]
public sealed class PrivacyMemoryServiceTests
{
    private string _root = "";

    [TestInitialize]
    public void Init() => _root = Path.Combine(Path.GetTempPath(), "privacy-" + Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public async Task ForgetPerson_Removes_Entity_Folder()
    {
        var people = Path.Combine(_root, "scenarios", "s1", "people", "ryan");
        Directory.CreateDirectory(people);
        File.WriteAllText(Path.Combine(people, "profile.md"), "# Ryan");

        var svc = new PrivacyMemoryService();
        var ok = await svc.ForgetPersonAsync(_root, "s1", "ryan");
        Assert.IsTrue(ok);
        Assert.IsFalse(Directory.Exists(people));
    }

    [TestMethod]
    public async Task Settings_RoundTrip()
    {
        var svc = new PrivacyMemoryService();
        await svc.UpdateSettingsAsync(_root, new CompanionPrivacySettings { AutoIngestOnSessionEnd = false });
        var loaded = await svc.GetSettingsAsync(_root);
        Assert.IsFalse(loaded.AutoIngestOnSessionEnd);
    }
}
