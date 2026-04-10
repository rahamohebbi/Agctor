using AgctorSDK.Host.Services.Sessions;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class SqliteSessionStoreProjectIntegrationTests
{
    [Fact]
    public async Task AssignAndDetachProject_WorksForSession()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"agctor-session-project-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteSessionStore(dbPath);
            var session = await store.CreateSessionAsync(title: "session-a");
            var project = await store.CreateProjectAsync(name: "People A", scenarioId: "people");
            project.ScenarioId.Should().Be("people");

            await store.AssignSessionToProjectAsync(session.SessionId, project.ProjectId);
            var loaded = await store.GetSessionAsync(session.SessionId);
            loaded.Should().NotBeNull();
            loaded!.ProjectId.Should().Be(project.ProjectId);

            var inProject = await store.ListSessionsByProjectAsync(project.ProjectId);
            inProject.Should().Contain(x => x.SessionId == session.SessionId);

            await store.DetachSessionFromProjectAsync(session.SessionId);
            var detached = await store.GetSessionAsync(session.SessionId);
            detached!.ProjectId.Should().BeNull();
            var standalone = await store.ListStandaloneSessionsAsync();
            standalone.Should().Contain(x => x.SessionId == session.SessionId);
        }
        finally
        {
            try { File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public async Task DeleteProject_DetachesSessions()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"agctor-session-project-del-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteSessionStore(dbPath);
            var s1 = await store.CreateSessionAsync(title: "s1");
            var s2 = await store.CreateSessionAsync(title: "s2");
            var project = await store.CreateProjectAsync(name: "People B", scenarioId: "people");
            project.ScenarioId.Should().Be("people");

            await store.AssignSessionToProjectAsync(s1.SessionId, project.ProjectId);
            await store.AssignSessionToProjectAsync(s2.SessionId, project.ProjectId);

            await store.DeleteProjectAsync(project.ProjectId);

            (await store.GetProjectAsync(project.ProjectId)).Should().BeNull();
            (await store.GetSessionAsync(s1.SessionId))!.ProjectId.Should().BeNull();
            (await store.GetSessionAsync(s2.SessionId))!.ProjectId.Should().BeNull();
        }
        finally
        {
            try { File.Delete(dbPath); } catch { }
        }
    }
}
