using System.Net;
using System.Net.Http.Json;
using AgctorSDK.Core.SceneMemory;
using AgctorSDK.Host.Models.SceneMemory;
using AgctorSDK.Host.Services;
using Xunit;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class SceneShotDesignTests
{
    private static SceneWorkspace Apply(SceneWorkspace w, string id) => ScenePromptReducer.Apply(w,
        new(id, ScenePromptReducer.Revision(w), "Maya smiles.", id, "test", new([], [], [], [new("Maya smiles", [w.Entities[0].Id], "Maya smiles.")])));
    private static SceneWorkspace Initial(Dictionary<string,string>? defaults = null) => new("w", "Story", DateTimeOffset.UtcNow,
        [new("maya", "Maya", "Character", new("maya", "asset", 1, "Maya design", defaults ?? new() { ["appearance"] = "Blue coat" }))], []);

    [Fact]
    public void Apply_copies_defaults_and_idempotent_retry_never_repins()
    {
        var defaults = new Dictionary<string,string> { ["appearance"] = "Blue coat" };
        var w = Apply(Initial(defaults), "one");
        defaults["appearance"] = "Red coat";
        Assert.Equal("Blue coat", w.Prompts![0].DesignSnapshots![0].Studio!.Defaults["appearance"]);
        Assert.Single(SceneShotDesigns.Review(w, "one").Updates);
        Assert.Same(w, Apply(w, "one"));
    }

    [Theory]
    [InlineData("Character")][InlineData("Object")][InlineData("Location")]
    public void Generation_follows_latest_links_while_historical_snapshots_remain_intact(string kind)
    {
        var w = Initial();
        w = w with { Entities = [w.Entities[0] with { Kind = kind }] };
        w = Apply(Apply(w, "one"), "two");
        var history = w.History;
        w = w with { Entities = [w.Entities[0] with { Studio = new("new", "replacement", 1, "Replacement", new Dictionary<string,string> { ["appearance"] = "Red coat" }) }] };
        Assert.Contains("Red coat", SceneVideoPromptBuilder.Prepare(w, "one", ScenePromptReducer.Revision(w)).Prompt);
        var stale = ScenePromptReducer.Revision(w);
        w = SceneShotDesigns.Pin(w, "two", stale);
        Assert.Same(history, w.History);
        Assert.Contains("Red coat", SceneVideoPromptBuilder.Prepare(w, "one", ScenePromptReducer.Revision(w)).Prompt);
        Assert.Contains("Red coat", SceneVideoPromptBuilder.Prepare(w, "two", ScenePromptReducer.Revision(w)).Prompt);
        Assert.Throws<InvalidOperationException>(() => SceneShotDesigns.Pin(w, "one", stale));
        Assert.Same(w, SceneShotDesigns.Pin(w, "two", ScenePromptReducer.Revision(w)));
        w = w with { Entities = [w.Entities[0] with { Studio = null }] };
        var diff = Assert.Single(SceneShotDesigns.Review(w, "two").Updates);
        Assert.NotNull(diff.Saved); Assert.Null(diff.Current);
        Assert.DoesNotContain("Red coat", SceneVideoPromptBuilder.Prepare(w, "two", ScenePromptReducer.Revision(w)).Prompt);
        w = SceneShotDesigns.Pin(w, "two", ScenePromptReducer.Revision(w));
        Assert.Null(w.Prompts![1].DesignSnapshots![0].Studio);
        Assert.DoesNotContain("Red coat", SceneVideoPromptBuilder.Prepare(w, "two", ScenePromptReducer.Revision(w)).Prompt);
    }

    [Fact]
    public void Links_update_participating_designs_without_adding_unrelated_future_designs()
    {
        var w = Initial(); w = w with { Entities = [w.Entities[0] with { Studio = null }] };
        w = Apply(w, "one");
        w = w with { Entities = [Initial().Entities[0], new("future", "Future object", "Object", new("future", "future", 1, "Future", new Dictionary<string,string>()))] };
        var review = SceneShotDesigns.Review(w, "one");
        Assert.True(review.IsPinned); Assert.Single(review.Current); Assert.Single(review.Updates);
        Assert.Contains("Blue coat", SceneVideoPromptBuilder.Prepare(w, "one", ScenePromptReducer.Revision(w)).Prompt);
        Assert.DoesNotContain(SceneVideoPromptBuilder.Participants(w, "one"), e => e.Id == "future");
        w = SceneShotDesigns.Pin(w, "one", review.Revision);
        Assert.Single(w.Prompts![0].DesignSnapshots!);
        Assert.Contains("Blue coat", SceneVideoPromptBuilder.Prepare(w, "one", ScenePromptReducer.Revision(w)).Prompt);
    }

    [Fact]
    public async Task Snapshots_and_explicit_updates_survive_restarting_the_store()
    {
        var directory = Path.Combine(Path.GetTempPath(), "scene-pins-" + Guid.NewGuid().ToString("N"));
        try {
            var path = Path.Combine(directory, "scene.db");
            var store = new SqliteSceneMemoryStore(path);
            var w = await store.ConnectAsync("Story", new("cynelo", "p", "s"));
            w = await store.SyncStudioAsync(w.Id, "maya", new("asset", 1, "Maya", "Character", new Dictionary<string,string> { ["appearance"] = "Blue" }));
            var preview = new ScenePromptPreview("one", ScenePromptReducer.Revision(w), "Maya smiles.", "One", "test", new([], [], [], [new("Maya smiles", [w.Entities[0].Id], "Maya smiles.")]));
            w = await store.ApplyPromptAsync(w.Id, preview);
            w = await store.SyncStudioAsync(w.Id, "maya", new("asset", 2, "Maya", "Character", new Dictionary<string,string> { ["appearance"] = "Red" }));
            store = new SqliteSceneMemoryStore(path); w = await store.GetAsync(w.Id);
            Assert.Equal("Blue", w.Prompts![0].DesignSnapshots![0].Studio!.Defaults["appearance"]);
            w = await store.PinShotDesignsAsync(w.Id, "one", ScenePromptReducer.Revision(w));
            var reopened = await new SqliteSceneMemoryStore(path).GetAsync(w.Id);
            Assert.Equal("Red", reopened.Prompts![0].DesignSnapshots![0].Studio!.Defaults["appearance"]);
            Assert.Single(reopened.History);
        } finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}

[Collection("LlmStatic")]
public sealed class SceneShotDesignHttpTests(SceneStudioWebApplicationFactory factory) : IClassFixture<SceneStudioWebApplicationFactory>
{
    [Fact]
    public async Task Apply_pins_and_update_is_revision_checked_over_the_public_HTTP_contract()
    {
        var client = factory.CreateClient();
        async Task<T> Read<T>(HttpResponseMessage r) { Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return (await r.Content.ReadFromJsonAsync<T>())!; }
        var w = await Read<SceneMemoryWorkspaceResponse>(await client.PostAsJsonAsync("/api/scene-memory/v1/workspaces/connect", new ConnectStoryRequest("Pins", "cynelo", Guid.NewGuid().ToString("N"), "story")));
        var path = $"/api/scene-memory/v1/workspaces/{w.Id}";
        w = await Read<SceneMemoryWorkspaceResponse>(await client.PutAsJsonAsync(path + "/studio-entities/maya", new SyncStudioRequest("asset", 1, "Maya", "Character", new(){["appearance"]="Blue"})));
        var p = await Read<SceneMemoryPreviewResponse>(await client.PostAsJsonAsync(path + "/previews", new InterpretSceneRequest("Maya holds a mug.", Model:"test")));
        w = await Read<SceneMemoryWorkspaceResponse>(await client.PostAsync(path + $"/previews/{p.Preview.Id}/apply", null));
        var shotPath = path + $"/shots/{p.Preview.Id}/designs";
        var original = await Read<SceneShotDesignReview>(await client.GetAsync(shotPath));
        Assert.True(original.IsPinned); Assert.Empty(original.Updates);
        w = await Read<SceneMemoryWorkspaceResponse>(await client.PutAsJsonAsync(path + "/studio-entities/maya", new SyncStudioRequest("asset", 2, "Maya", "Character", new(){["appearance"]="Red"})));
        var review = await Read<SceneShotDesignReview>(await client.GetAsync(shotPath));
        Assert.Single(review.Updates);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(shotPath, new PrepareSceneVideoRequest(original.Revision))).StatusCode);
        var timeline = await Read<SceneTimelineResponse>(await client.PutAsJsonAsync(shotPath, new PrepareSceneVideoRequest(review.Revision)));
        Assert.Empty(timeline.Shots[0].Designs.Updates);
        Assert.Equal("Red", timeline.Shots[0].Designs.Saved.Single(e=>e.Name=="Maya").Studio!.Defaults["appearance"]);
        var final = await Read<SceneMemoryWorkspaceResponse>(await client.GetAsync(path));
        Assert.Equal(w.LatestSequence, final.LatestSequence);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path+"/shots/missing/designs")).StatusCode);
    }
}
