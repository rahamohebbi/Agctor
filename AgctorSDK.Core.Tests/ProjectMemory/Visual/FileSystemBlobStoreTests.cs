using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Visual;
using AgctorSDK.Core.ProjectMemory.Visual.Storage;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.ProjectMemory.Visual;

[TestClass]
public sealed class FileSystemBlobStoreTests
{
    [TestMethod]
    public async Task WriteObject_uses_project_agctor_visual_blobs_when_project_root_set()
    {
        var root = Path.Combine(Path.GetTempPath(), "agctor-blob-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".agctor"));
        await File.WriteAllTextAsync(Path.Combine(root, ".agctor", "project.yaml"), "projectId: test\n");

        var projectOptions = Options.Create(new ProjectMemoryAgentOptions { ProjectRoot = root });
        var monitor = new TestOptionsMonitor<ProjectMemoryAgentOptions>(projectOptions.Value);
        var store = new FileSystemBlobStore(
            Options.Create(new VisualStorageOptions { Provider = "file" }),
            monitor);

        try
        {
            await store.WriteObjectAsync(
                "agctor-visual",
                "projects/p1/scenarios/s1/assets/a1/original.jpg",
                new MemoryStream(new byte[] { 1, 2, 3 }),
                CancellationToken.None);

            var expected = Path.Combine(root, ".agctor", "visual-blobs", "agctor-visual", "projects", "p1", "scenarios", "s1", "assets", "a1", "original.jpg");
            File.Exists(expected).Should().BeTrue("blobs should persist under the project, not Host bin/");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T> where T : class
    {
        public TestOptionsMonitor(T value) => CurrentValue = value;
        public T CurrentValue { get; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
