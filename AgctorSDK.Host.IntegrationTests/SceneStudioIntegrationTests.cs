using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgctorSDK.Core.SceneMemory;
using AgctorSDK.Host.Models.SceneMemory;
using AgctorSDK.Host.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgctorSDK.Host.IntegrationTests;

[Collection("LlmStatic")]
public sealed class SceneStudioIntegrationTests : IClassFixture<SceneStudioWebApplicationFactory>
{
    private const string Root = "/api/scene-memory/v1";
    private readonly HttpClient _client;
    public SceneStudioIntegrationTests(SceneStudioWebApplicationFactory factory) => _client = factory.CreateClient();

    private async Task<T> Read<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private Task<SceneMemoryWorkspaceResponse> Connect(string? project = null, string story = "story-1") => ConnectRequest(
        new("Studio integration", "cynelo", project ?? Guid.NewGuid().ToString("N"), story));
    private async Task<SceneMemoryWorkspaceResponse> ConnectRequest(ConnectStoryRequest body) =>
        await Read<SceneMemoryWorkspaceResponse>(await _client.PostAsJsonAsync(Root + "/workspaces/connect", body));
    private string Path(string id) => Root + "/workspaces/" + id;
    private async Task<SceneMemoryWorkspaceResponse> Sync(string id, string key, SyncStudioRequest input) =>
        await Read<SceneMemoryWorkspaceResponse>(await _client.PutAsJsonAsync(Path(id) + "/studio-entities/" + key, input));
    private async Task<SceneMemoryPreviewResponse> Preview(string id, string prompt = "Maya holds a mug.") =>
        await Read<SceneMemoryPreviewResponse>(await _client.PostAsJsonAsync(Path(id) + "/previews",
            new InterpretSceneRequest(prompt, "A new prop", "Kitchen", 5, "test-studio")));

    [Fact]
    public async Task Complete_HTTP_path_connect_studios_discover_review_apply_link_and_read_timeline()
    {
        var w = await Connect();
        w = await Sync(w.Id, "maya", new("character-42", 1, "Maya", "Character", new() { ["appearance"] = "Blue coat" }));
        w = await Sync(w.Id, "kitchen", new("location-9", 1, "Kitchen", "Location", new() { ["style"] = "Bright cartoon kitchen" }));
        var characterId = w.Entities.Single(e => e.Name == "Maya").Id;
        var review = await Preview(w.Id);
        Assert.Equal(3, review.Proposed.Entities.Count);
        Assert.Null(review.Proposed.Entities.Single(e => e.Name == "Mug").Studio);
        var unchanged = (await _client.GetFromJsonAsync<SceneMemoryWorkspaceResponse>(Path(w.Id)))!;
        Assert.Equal(0, unchanged.LatestSequence);
        Assert.Equal(2, unchanged.Entities.Count);
        // Review and edit use JSON alone, just as a separate Cynelo client would.
        review = await Read<SceneMemoryPreviewResponse>(await _client.PutAsJsonAsync(Path(w.Id) + "/previews/" + review.Preview.Id,
            new EditScenePreviewRequest(review.Preview.Extraction)));
        var saved = await Read<SceneMemoryWorkspaceResponse>(await _client.PostAsync(Path(w.Id) + "/previews/" + review.Preview.Id + "/apply", null));
        var mug = saved.Entities.Single(e => e.Name == "Mug");
        saved = await Sync(w.Id, "mug-instance", new("object-7", 1, "Ceramic studio mug", "Object", new() { ["color"] = "Red" }, mug.Id));
        Assert.Equal("Mug", saved.Entities.Single(e => e.Id == mug.Id).Name); // Linking does not rewrite history names.
        var query = await Read<SceneQueryResult>(await _client.PostAsJsonAsync(Path(w.Id) + "/query", new SceneQuery(mug.Id)));
        Assert.Equal(characterId, Assert.Single(query.Facts).RelatedEntityId);
        var timeline = (await _client.GetFromJsonAsync<SceneTimelineResponse>(Path(w.Id) + "/timeline"))!;
        var shot = Assert.Single(timeline.Shots);
        Assert.Equal("Kitchen", shot.SceneName);
        Assert.Equal("A new prop", shot.ShotName);
        Assert.Equal(5, shot.DurationSeconds);
        Assert.Single(shot.IntroducedEntityIds);
        Assert.Equal(mug.Id, shot.IntroducedEntityIds[0]);
        Assert.Equal(characterId, Assert.Single(shot.StateAfter).RelatedEntityId);
        Assert.Null(Assert.Single(shot.Changes).Before);
        Assert.Equal("held_by", shot.Changes[0].Property);
        Assert.Equal("object-7", timeline.Entities.Single(e => e.Id == mug.Id).Studio!.AssetId);
        var retry = await Read<SceneMemoryWorkspaceResponse>(await _client.PostAsync(Path(w.Id) + "/previews/" + review.Preview.Id + "/apply", null));
        Assert.Equal(saved.LatestSequence, retry.LatestSequence);
    }

    [Fact]
    public async Task Connect_is_idempotent_and_project_and_story_scoped()
    {
        var project = Guid.NewGuid().ToString("N");
        var first = await Connect(project);
        Assert.Equal(first.Id, (await Connect(project)).Id);
        Assert.NotEqual(first.Id, (await Connect(project, "story-2")).Id);
        Assert.NotEqual(first.Id, (await Connect()).Id);
        var listed = (await _client.GetFromJsonAsync<SceneMemoryWorkspaceResponse[]>(Root + "/workspaces?application=cynelo&projectId=" + project))!;
        Assert.Equal(2, listed.Length);
        Assert.All(listed, w => Assert.Equal(project, w.Connection!.ProjectId));
        Assert.Empty((await _client.GetFromJsonAsync<SceneMemoryWorkspaceResponse[]>(Root + "/workspaces?application=other&projectId=" + project))!);
    }

    [Fact]
    public async Task Existing_workspace_can_be_connected_without_losing_entities_or_history_and_cannot_be_rebound()
    {
        var legacy = await Read<SceneWorkspace>(await _client.PostAsJsonAsync("/api/scene-memory/workspaces", new { name = "Existing story" }));
        legacy = await Read<SceneWorkspace>(await _client.PostAsJsonAsync($"/api/scene-memory/workspaces/{legacy.Id}/entities", new { name = "Maya", kind = "Character" }));
        legacy = await Read<SceneWorkspace>(await _client.PostAsJsonAsync($"/api/scene-memory/workspaces/{legacy.Id}/facts", new { entityId = legacy.Entities[0].Id, property = "mood", value = "happy" }));
        var body = new ConnectStoryRequest("Unused rename", "cynelo", Guid.NewGuid().ToString("N"), "story", legacy.Id);
        var connected = await ConnectRequest(body);
        Assert.Equal(legacy.Id, connected.Id);
        Assert.Equal(legacy.Name, connected.Name);
        Assert.Equal(1, connected.LatestSequence);
        Assert.Equal(legacy.Entities[0].Id, connected.Entities[0].Id);
        var wrong = await _client.PostAsJsonAsync(Root + "/workspaces/connect", body with { StoryId = "other" });
        Assert.Equal(HttpStatusCode.Conflict, wrong.StatusCode);
        var another = await Connect();
        var duplicate = await _client.PostAsJsonAsync(Root + "/workspaces/connect", body with { WorkspaceId = another.Id });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Studio_retries_are_safe_and_versions_cannot_overwrite_newer_content()
    {
        var w = await Connect();
        var input = new SyncStudioRequest("character", 2, "Maya", "Character", new() { ["costume"] = "Blue" });
        w = await Sync(w.Id, "maya", input);
        Assert.Equal(w.Revision, (await Sync(w.Id, "maya", input)).Revision);
        foreach (var invalid in new[] { input with { Version = 1 }, input with { Name = "Different" }, input with { Version = 3, AssetId = "other" } })
        {
            var response = await _client.PutAsJsonAsync(Path(w.Id) + "/studio-entities/maya", invalid);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        }
        var updated = await Sync(w.Id, "maya", input with { Version = 3, Name = "Maya new design" });
        Assert.Equal(w.Entities[0].Id, updated.Entities[0].Id);
        Assert.Equal("Maya", updated.Entities[0].Name);
        Assert.Equal("Maya new design", updated.Entities[0].Studio!.DisplayName);
    }

    [Fact]
    public async Task Same_asset_supports_distinct_instances_and_names_do_not_implicitly_merge()
    {
        var w = await Connect();
        w = await Sync(w.Id, "chair-1", new("chair-model", 1, "Left chair", "Object"));
        w = await Sync(w.Id, "chair-2", new("chair-model", 1, "Right chair", "Object"));
        Assert.Equal(2, w.Entities.Count);
        Assert.Equal(2, w.Entities.Select(e => e.Id).Distinct().Count());
        var conflict = await _client.PutAsJsonAsync(Path(w.Id) + "/studio-entities/chair-3", new SyncStudioRequest("chair-model", 1, "Left chair", "Object"));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var other = await Connect();
        var foreign = await _client.PutAsJsonAsync(Path(other.Id) + "/studio-entities/chair", new SyncStudioRequest("chair-model", 1, "Chair", "Object", EntityId: w.Entities[0].Id));
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        var relink = await _client.PutAsJsonAsync(Path(w.Id) + "/studio-entities/chair-3", new SyncStudioRequest("chair-model", 1, "Chair", "Object", EntityId: w.Entities[0].Id));
        Assert.Equal(HttpStatusCode.Conflict, relink.StatusCode);
    }

    [Fact]
    public async Task Studio_sync_invalidates_stale_preview_but_never_resets_story_state()
    {
        var w = await Connect();
        var input = new SyncStudioRequest("character", 1, "Maya", "Character");
        w = await Sync(w.Id, "maya", input);
        var stale = await Preview(w.Id);
        w = await Sync(w.Id, "maya", input with { Version = 2, Defaults = new() { ["holding"] = "nothing" } });
        var conflict = await _client.PostAsync(Path(w.Id) + "/previews/" + stale.Preview.Id + "/apply", null);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var review = await Preview(w.Id);
        var applied = await Read<SceneMemoryWorkspaceResponse>(await _client.PostAsync(Path(w.Id) + "/previews/" + review.Preview.Id + "/apply", null));
        var synced = await Sync(w.Id, "maya", input with { Version = 3, Defaults = new() { ["holding"] = "a sword" } });
        Assert.Equal(applied.LatestSequence, synced.LatestSequence);
        var mugId = synced.Entities.Single(e => e.Name == "Mug").Id;
        var query = await Read<SceneQueryResult>(await _client.PostAsJsonAsync(Path(w.Id) + "/query", new SceneQuery(mugId)));
        Assert.Equal("Maya", Assert.Single(query.Facts).Value);
    }

    [Fact]
    public async Task Timeline_keeps_historical_state_when_followup_releases_object()
    {
        var w = await Connect();
        await Sync(w.Id, "maya", new("character", 1, "Maya", "Character"));
        foreach (var text in new[] { "Maya holds a mug.", "She drops it." })
        {
            var review = await Preview(w.Id, text);
            await Read<SceneMemoryWorkspaceResponse>(await _client.PostAsync(Path(w.Id) + "/previews/" + review.Preview.Id + "/apply", null));
        }
        var timeline = (await _client.GetFromJsonAsync<SceneTimelineResponse>(Path(w.Id) + "/timeline"))!;
        Assert.Equal(2, timeline.Shots.Count);
        Assert.Equal("held_by", Assert.Single(timeline.Shots[0].StateAfter).Property);
        Assert.Empty(timeline.Shots[1].StateAfter);
        Assert.NotNull(Assert.Single(timeline.Shots[1].Changes).Before);
        Assert.Null(timeline.Shots[1].Changes[0].After);
    }

    [Fact]
    public async Task Dedicated_OpenAPI_contains_only_Scene_Memory_integration_and_typed_results()
    {
        var api = await _client.GetFromJsonAsync<JsonElement>("/swagger/scene-memory-v1/swagger.json");
        var paths = api.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToArray();
        Assert.NotEmpty(paths);
        Assert.All(paths, p => Assert.StartsWith(Root + "/", p));
        Assert.DoesNotContain(paths, p => p.Contains("agents") || p.Contains("evaluations"));
        Assert.Contains(Root + "/workspaces/{workspaceId}/timeline", paths);
        Assert.Contains("SceneTimelineResponse", api.GetProperty("components").GetProperty("schemas").ToString());
        var general = await _client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        Assert.DoesNotContain(general.GetProperty("paths").EnumerateObject(), p => p.Name.StartsWith(Root + "/"));
        var ui = await _client.GetStringAsync("/swagger/scene-memory/index.html");
        Assert.Contains("Scene Memory integration API", ui);
        var capabilities = (await _client.GetFromJsonAsync<SceneMemoryCapabilitiesResponse>(Root + "/capabilities"))!;
        Assert.False(capabilities.PersistentJobs);
        Assert.Equal(20, capabilities.PreviewLifetimeMinutes);
    }

    [Theory]
    [InlineData("Audio", 1)]
    [InlineData("Character", 0)]
    public async Task Invalid_studio_input_does_not_create_an_entity(string kind, int version)
    {
        var w = await Connect();
        var response = await _client.PutAsJsonAsync(Path(w.Id) + "/studio-entities/bad", new SyncStudioRequest("asset", version, "Bad", kind));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty((await _client.GetFromJsonAsync<SceneMemoryWorkspaceResponse>(Path(w.Id)))!.Entities);
    }
}

public sealed class SceneStudioWebApplicationFactory : SceneMemoryWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services => services.AddSingleton<IScenePromptInterpreter, StudioInterpreter>());
    }

    private sealed class StudioInterpreter : IScenePromptInterpreter
    {
        public Task<ScenePromptExtraction> InterpretAsync(SceneWorkspace w, string prompt, string model, CancellationToken ct)
        {
            var character = w.Entities.Single(e => e.Name == "Maya");
            Assert.NotNull(character.Studio);
            if (prompt == "She drops it.") return Task.FromResult(new ScenePromptExtraction([], [new(w.Entities.Single(e => e.Name == "Mug").Id, "held_by", "", "", true, prompt)], []));
            return Task.FromResult(new ScenePromptExtraction([new("new-mug", "Mug", "Object")],
                [new("new-mug", "held_by", "", character.Id, false, prompt)], []));
        }
    }
}
