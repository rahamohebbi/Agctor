using AgctorSDK.Host.Services.Sessions;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class SqliteSessionStoreFocusIntegrationTests
{
    [Fact]
    public async Task CreateProject_PersistsFocusEntity()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"agctor-focus-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteSessionStore(dbPath);
            var project = await store.CreateProjectAsync(
                name: "Mom",
                scenarioId: "people",
                focusEntityKey: "raha",
                focusDisplayName: "Raha Mohebbi");

            project.FocusEntityKey.Should().Be("raha");
            project.FocusDisplayName.Should().Be("Raha Mohebbi");

            var loaded = await store.GetProjectAsync(project.ProjectId);
            loaded!.FocusEntityKey.Should().Be("raha");
        }
        finally
        {
            try { File.Delete(dbPath); } catch { }
        }
    }
}
