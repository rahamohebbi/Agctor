using System;
using System.IO;
using System.Threading.Tasks;
using AgctorSDK.Core.DependencyInjection;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Indexing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.IntegrationTests.ProjectMemory;

[TestClass]
public sealed class ProjectMemoryRebuildIntegrationTests
{
    private static string SampleRoot()
    {
        var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        return Path.Combine(repo, "samples", "people-project");
    }

    [TestMethod]
    public async Task Rebuild_SamplePeopleProject_Succeeds()
    {
        var root = SampleRoot();
        Assert.IsTrue(Directory.Exists(Path.Combine(root, ".agctor")), $"Missing sample at {root}");

        var services = new ServiceCollection();
        services.AddAgctorProjectMemory();
        services.Configure<ProjectMemoryAgentOptions>(o => o.ProjectRoot = root);
        var sp = services.BuildServiceProvider();

        var coord = sp.GetRequiredService<RebuildCoordinator>();
        var report = await coord.RebuildAsync(root).ConfigureAwait(false);

        Assert.IsTrue(report.Success, string.Join("; ", report.Issues.ConvertAll(i => i.Message)));
        var db = Path.Combine(root, ".agctor", "runtime", "sqlite", "agctor.db");
        Assert.IsTrue(File.Exists(db), $"SQLite index not created at {db}");
    }
}
