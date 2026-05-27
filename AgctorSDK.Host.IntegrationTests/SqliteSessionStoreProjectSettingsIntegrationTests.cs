using AgctorSDK.Core.Sessions;
using AgctorSDK.Core.Sessions.Models;
using AgctorSDK.Host.Services.Sessions;
using FluentAssertions;
using Microsoft.Data.Sqlite;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class SqliteSessionStoreProjectSettingsIntegrationTests
{
    [Fact]
    public async Task UpdateProject_Persists_VisualMaxPhotos_In_SettingsJson()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "agctor-settings-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var store = new SqliteSessionStore(dbPath);
            var project = await store.CreateProjectAsync(name: "Raha", scenarioId: "person_3");

            var updated = await store.UpdateProjectAsync(new SessionProject
            {
                ProjectId = project.ProjectId,
                Name = project.Name,
                ScenarioId = project.ScenarioId,
                SettingsJson = new ChatProjectSettings { VisualMaxPhotos = 5 }.ToJson()
            });

            updated.VisualMaxPhotos.Should().Be(5);

            var loaded = await store.GetProjectAsync(project.ProjectId);
            loaded!.VisualMaxPhotos.Should().Be(5);
            ChatProjectSettings.FromJson(loaded.SettingsJson).VisualMaxPhotos.Should().Be(5);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }
}
