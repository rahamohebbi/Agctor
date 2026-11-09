using System.Net;
using System.Net.Http.Json;
using AgctorSDK.Core.SceneMemory;
using AgctorSDK.Host.Models.SceneMemory;
using Xunit;

namespace AgctorSDK.Host.IntegrationTests;

[Collection("LlmStatic")]
public sealed class SceneLinkManagementTests : IClassFixture<SceneStudioWebApplicationFactory>
{
    private readonly HttpClient _client;
    public SceneLinkManagementTests(SceneStudioWebApplicationFactory factory) => _client = factory.CreateClient();
    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private async Task<SceneMemoryWorkspaceResponse> Create(string kind) {
        var story = await Read<SceneMemoryWorkspaceResponse>(await _client.PostAsJsonAsync("/api/scene-memory/v1/workspaces/connect", new ConnectStoryRequest("Links", "cynelo", Guid.NewGuid().ToString("N"), "story")));
        return await Read<SceneMemoryWorkspaceResponse>(await _client.PutAsJsonAsync($"/api/scene-memory/v1/workspaces/{story.Id}/studio-entities/old", new SyncStudioRequest("old-asset",1,"Scene item",kind,new(){["appearance"]="Original"})));
    }
    private string Path(SceneMemoryWorkspaceResponse w, string entity) => $"/api/scene-memory/v1/workspaces/{w.Id}/entities/{entity}/studio-link";

    [Theory]
    [InlineData("Character")][InlineData("Object")][InlineData("Location")]
    public async Task Replace_unlink_and_relink_preserve_entity_name_facts_and_history(string kind)
    {
        var w = await Create(kind); var id = w.Entities.Single().Id;
        await Read<SceneWorkspace>(await _client.PostAsJsonAsync($"/api/scene-memory/workspaces/{w.Id}/facts",new {entityId=id,property="description",value="Saved story fact"}));
        w = (await _client.GetFromJsonAsync<SceneMemoryWorkspaceResponse>($"/api/scene-memory/v1/workspaces/{w.Id}"))!;
        var oldRevision=w.Revision;
        w=await Read<SceneMemoryWorkspaceResponse>(await _client.PutAsJsonAsync(Path(w,id),new ChangeSceneDesignRequest(w.Revision,"new",new("new-asset",1,"New library name",kind,new(){["appearance"]="New design"}))));
        Assert.Equal(id, w.Entities.Single().Id); Assert.Equal("Scene item",w.Entities.Single().Name);
        Assert.Equal("new-asset", w.Entities.Single().Studio!.AssetId); Assert.Equal(1,w.LatestSequence);
        Assert.NotEqual(oldRevision,w.Revision);
        Assert.Equal(HttpStatusCode.Conflict,(await _client.PutAsJsonAsync(Path(w,id),new ChangeSceneDesignRequest(oldRevision))).StatusCode);
        w=await Read<SceneMemoryWorkspaceResponse>(await _client.PutAsJsonAsync(Path(w,id),new ChangeSceneDesignRequest(w.Revision)));
        Assert.Null(w.Entities.Single().Studio); Assert.Equal(1,w.LatestSequence);
        w=await Read<SceneMemoryWorkspaceResponse>(await _client.PutAsJsonAsync(Path(w,id),new ChangeSceneDesignRequest(w.Revision,"old",new("old-asset",1,"Original again",kind))));
        Assert.Equal("old-asset",w.Entities.Single().Studio!.AssetId);
        var query=await Read<SceneQueryResult>(await _client.PostAsJsonAsync($"/api/scene-memory/v1/workspaces/{w.Id}/query",new SceneQuery(id)));
        Assert.Equal("Saved story fact",Assert.Single(query.Facts).Value);
    }

    [Fact]
    public async Task Invalid_replacement_or_instance_collision_leaves_current_link_intact()
    {
        var w=await Create("Character"); var id=w.Entities.Single().Id;
        w=await Read<SceneMemoryWorkspaceResponse>(await _client.PutAsJsonAsync($"/api/scene-memory/v1/workspaces/{w.Id}/studio-entities/other",new SyncStudioRequest("other-asset",1,"Other character","Character")));
        foreach(var request in new[]{
            new ChangeSceneDesignRequest(w.Revision,"wrong-kind",new("object",1,"Object","Object")),
            new ChangeSceneDesignRequest(w.Revision,"other",new("other-asset",1,"Other character","Character"))})
            Assert.Equal(HttpStatusCode.Conflict,(await _client.PutAsJsonAsync(Path(w,id),request)).StatusCode);
        var after=(await _client.GetFromJsonAsync<SceneMemoryWorkspaceResponse>($"/api/scene-memory/v1/workspaces/{w.Id}"))!;
        Assert.Equal(w.Revision,after.Revision);
        Assert.Equal("old-asset",after.Entities.Single(e=>e.Id==id).Studio!.AssetId);
        Assert.Equal(HttpStatusCode.NotFound,(await _client.PutAsJsonAsync(Path(w,"missing"),new ChangeSceneDesignRequest(w.Revision))).StatusCode);
    }

    [Fact]
    public async Task Unlink_persists_after_restart_and_keeps_history()
    {
        var folder=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"scene-unlink-"+Guid.NewGuid().ToString("N"));
        var db=System.IO.Path.Combine(folder,"scene.db");
        try {
            var store=new SqliteSceneMemoryStore(db);
            var w=await store.ConnectAsync("Story",new("test","project","story"));
            w=await store.SyncStudioAsync(w.Id,"actor",new("design",1,"Actor","Character"));
            var id=w.Entities.Single().Id;
            w=await store.RecordAsync(w.Id,id,"mood","happy",null);
            w=await store.ChangeStudioLinkAsync(w.Id,id,ScenePromptReducer.Revision(w),null,null);
            var after=await new SqliteSceneMemoryStore(db).GetAsync(w.Id);
            Assert.Null(after.Entities.Single().Studio);Assert.Equal(id,after.Entities.Single().Id);Assert.Single(after.History);
        } finally { if(Directory.Exists(folder)) Directory.Delete(folder,true); }
    }
}
