using System.Net;
using System.Net.Http.Json;
using AgctorSDK.Core.Sessions.Models;
using AgctorSDK.Host.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace AgctorSDK.Host.IntegrationTests;

/// <summary>HTTP tests for chat project CRUD and session association APIs.</summary>
public sealed class ChatProjectsControllerIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private static int _portCounter = 12180;

    public ChatProjectsControllerIntegrationTests(WebApplicationFactory<Program> factory)
    {
        var configured = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var uniquePort = Interlocked.Increment(ref _portCounter);
                config.AddInMemoryCollection(new[]
                {
                    new KeyValuePair<string, string?>("Mcp:Port", uniquePort.ToString())
                });
            });
        });
        _client = configured.CreateClient();
    }

    [Fact]
    public async Task ProjectCrudAndSessionAssociation_Works()
    {
        var createProj = await _client.PostAsJsonAsync("/api/chat/projects", new CreateChatProjectRequest
        {
            Name = "API Test Project",
            ScenarioId = "people"
        });
        createProj.StatusCode.Should().Be(HttpStatusCode.Created);
        var project = await createProj.Content.ReadFromJsonAsync<SessionProject>();
        project.Should().NotBeNull();
        project!.ProjectId.Should().NotBeNullOrWhiteSpace();
        project.ScenarioId.Should().Be("people");

        var listProj = await _client.GetAsync("/api/chat/projects?limit=20");
        listProj.StatusCode.Should().Be(HttpStatusCode.OK);
        var projects = await listProj.Content.ReadFromJsonAsync<List<SessionProject>>();
        projects.Should().Contain(p => p.ProjectId == project.ProjectId);

        var getProj = await _client.GetAsync($"/api/chat/projects/{project.ProjectId}");
        getProj.StatusCode.Should().Be(HttpStatusCode.OK);

        var putProj = await _client.PutAsJsonAsync($"/api/chat/projects/{project.ProjectId}", new UpdateChatProjectRequest
        {
            Name = "Renamed"
        });
        putProj.StatusCode.Should().Be(HttpStatusCode.OK);
        var renamed = await putProj.Content.ReadFromJsonAsync<SessionProject>();
        renamed!.Name.Should().Be("Renamed");

        var createSession = await _client.PostAsJsonAsync("/api/chat/sessions", new CreateChatSessionRequest
        {
            Title = "In project",
            ProjectId = project.ProjectId
        });
        createSession.StatusCode.Should().Be(HttpStatusCode.Created);
        var session = await createSession.Content.ReadFromJsonAsync<SessionInfo>();
        session!.ProjectId.Should().Be(project.ProjectId);

        var byProject = await _client.GetAsync($"/api/chat/sessions?projectId={Uri.EscapeDataString(project.ProjectId)}&limit=20");
        byProject.StatusCode.Should().Be(HttpStatusCode.OK);
        var inProject = await byProject.Content.ReadFromJsonAsync<List<SessionInfo>>();
        inProject.Should().Contain(s => s.SessionId == session.SessionId);

        var projSessions = await _client.GetAsync($"/api/chat/projects/{project.ProjectId}/sessions?limit=20");
        projSessions.StatusCode.Should().Be(HttpStatusCode.OK);
        var nested = await projSessions.Content.ReadFromJsonAsync<List<SessionInfo>>();
        nested.Should().Contain(s => s.SessionId == session.SessionId);

        var detach = await _client.DeleteAsync($"/api/chat/sessions/{session.SessionId}/project");
        detach.StatusCode.Should().Be(HttpStatusCode.OK);
        var detached = await detach.Content.ReadFromJsonAsync<SessionInfo>();
        detached!.ProjectId.Should().BeNull();

        var assign = await _client.PutAsJsonAsync($"/api/chat/sessions/{session.SessionId}/project", new AssignChatSessionProjectRequest
        {
            ProjectId = project.ProjectId
        });
        assign.StatusCode.Should().Be(HttpStatusCode.OK);
        var reattached = await assign.Content.ReadFromJsonAsync<SessionInfo>();
        reattached!.ProjectId.Should().Be(project.ProjectId);

        var delProj = await _client.DeleteAsync($"/api/chat/projects/{project.ProjectId}");
        delProj.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getAgain = await _client.GetAsync($"/api/chat/projects/{project.ProjectId}");
        getAgain.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var sessionAfter = await _client.GetAsync($"/api/chat/sessions/{session.SessionId}");
        sessionAfter.StatusCode.Should().Be(HttpStatusCode.OK);
        var tr = await sessionAfter.Content.ReadFromJsonAsync<SessionTranscript>();
        tr!.Session.ProjectId.Should().BeNull();
    }

    [Fact]
    public async Task ListSessions_StandaloneFilter_Works()
    {
        var createSession = await _client.PostAsJsonAsync("/api/chat/sessions", new CreateChatSessionRequest { Title = "Standalone list test" });
        createSession.StatusCode.Should().Be(HttpStatusCode.Created);
        var session = await createSession.Content.ReadFromJsonAsync<SessionInfo>();

        var standalone = await _client.GetAsync("/api/chat/sessions?standalone=true&limit=100");
        standalone.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await standalone.Content.ReadFromJsonAsync<List<SessionInfo>>();
        list.Should().Contain(s => s.SessionId == session!.SessionId);
    }

    [Fact]
    public async Task CreateSession_UnknownProject_Returns404()
    {
        var resp = await _client.PostAsJsonAsync("/api/chat/sessions", new CreateChatSessionRequest
        {
            ProjectId = "no-such-project-id"
        });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
