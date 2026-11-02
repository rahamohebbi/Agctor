using AgctorSDK.Core.SceneMemory;
using Xunit;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class SceneStudioPersistenceTests
{
    [Fact]
    public async Task Connections_and_bindings_survive_restart_and_concurrent_connect_creates_only_one_workspace()
    {
        var directory = Path.Combine(Path.GetTempPath(), "agctor-studio-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "scene.db");
        try
        {
            var store = new SqliteSceneMemoryStore(path);
            var connection = new SceneWorkspaceConnection("cynelo", "project", "story");
            var workspaces = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() =>
                new SqliteSceneMemoryStore(path).ConnectAsync("Story", connection))));
            Assert.Single(workspaces.Select(w => w.Id).Distinct());
            var w = await store.SyncStudioAsync(workspaces[0].Id, "actor", new("asset", 1, "Actor", "Character", new Dictionary<string, string> { ["appearance"] = "Blue" }));
            var reopened = new SqliteSceneMemoryStore(path);
            var resolved = await reopened.ConnectAsync("Story", connection);
            Assert.Equal(w.Id, resolved.Id);
            Assert.Equal(w.Entities[0].Id, resolved.Entities[0].Id);
            Assert.Equal("Blue", resolved.Entities[0].Studio!.Defaults["appearance"]);
            Assert.Single(await reopened.ListAsync());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
