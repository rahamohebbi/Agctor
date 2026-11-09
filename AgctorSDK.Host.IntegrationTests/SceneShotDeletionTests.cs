using System.Net;
using System.Net.Http.Json;
using AgctorSDK.Core.SceneMemory;
using AgctorSDK.Host.Models.SceneMemory;
using AgctorSDK.Host.Services;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class SceneShotDeletionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static SceneWorkspace Story() => new("w", "Story", Now,
        [new("maya", "Maya", "Character", CreatedAtSequence: 0), new("cup", "Cup", "Object", CreatedAtSequence: 2)],
        [new(1, "maya", "location", "Kitchen", "manual", Now),
         new(2, "maya", "mood", "Happy", "First", Now, PromptId: "one"),
         new(3, "maya", "location", "Garden", "Middle", Now, PromptId: "two"),
         new(4, "maya", "event", "Waves", "Middle", Now, PromptId: "two", IsEvent: true),
         new(5, "maya", "mood", "Calm", "Last", Now, PromptId: "three"),
         new(6, "cup", "color", "Blue", "manual after", Now)],
        [new("one", "First", "First", "test", 2, 2, Now, DesignSnapshots: []),
         new("two", "Middle", "Middle", "test", 3, 4, Now, DesignSnapshots: []),
         new("empty", "No changes", "Empty", "test", 5, 4, Now, DesignSnapshots: []),
         new("three", "Last", "Last", "test", 5, 5, Now, DesignSnapshots: [])]);

    [Fact]
    public void Middle_deletion_rebuilds_continuity_and_sequence_boundaries_without_rewriting_other_actions()
    {
        var original = Story();
        var updated = SceneShotDeletion.Delete(original, "two", ScenePromptReducer.Revision(original));
        Assert.Equal(new long[] { 1, 2, 3, 4 }, updated.History.Select(f => f.Sequence));
        Assert.DoesNotContain(updated.History, f => f.PromptId == "two");
        Assert.Equal(original.Entities, updated.Entities);
        Assert.Equal(new[] { "First", "No changes", "Last" }, updated.Prompts!.Select(p => p.Prompt));
        var timeline = SceneMemoryApiProjection.Timeline(updated);
        Assert.Equal(new[] { 1, 2, 3 }, timeline.Shots.Select(s => s.Order));
        Assert.Equal(3, updated.Prompts![1].FirstSequence);
        Assert.Equal(2, updated.Prompts[1].LastSequence);
        Assert.Equal(3, updated.Prompts[2].FirstSequence);
        Assert.Equal(3, updated.Prompts[2].LastSequence);
        Assert.Contains(timeline.Shots[2].StateAfter, f => f.Property == "location" && f.Value == "Kitchen");
        Assert.Contains(timeline.Shots[2].StateAfter, f => f.Property == "mood" && f.Value == "Calm");
        Assert.DoesNotContain(timeline.Shots[2].StateAfter, f => f.Note == "manual after");
        Assert.Equal(6, original.History.Count);
        Assert.Equal("Garden", original.History[2].Value);
    }

    [Fact]
    public void Collapsed_sequence_ranges_do_not_introduce_future_entities_into_legacy_shots()
    {
        var w = Story();
        w = w with {
            Entities = w.Entities.Append(new SceneEntity("future", "Future", "Character", CreatedAtSequence: 4)).ToArray(),
            Prompts = w.Prompts!.Select(p => p with { DesignSnapshots = null }).ToArray()
        };
        var deleted = SceneShotDeletion.Delete(w, "two", ScenePromptReducer.Revision(w));
        var review = SceneShotDesigns.Review(deleted, "one");
        Assert.False(review.IsPinned);
        Assert.DoesNotContain(review.Current, e => e.Id == "future");
        Assert.DoesNotContain(SceneVideoPromptBuilder.Participants(deleted, "one"), e => e.Id == "future");
        Assert.Contains(SceneShotDesigns.Review(deleted, "three").Current, e => e.Id == "future");
    }

    [Theory]
    [InlineData("one")][InlineData("three")][InlineData("empty")]
    public void First_last_and_empty_shots_can_be_deleted_and_stale_or_missing_deletes_do_not_mutate(string id)
    {
        var w = Story(); var revision = ScenePromptReducer.Revision(w);
        var updated = SceneShotDeletion.Delete(w, id, revision);
        Assert.DoesNotContain(updated.Prompts!, p => p.Id == id);
        Assert.NotEqual(revision, ScenePromptReducer.Revision(updated));
        Assert.Throws<InvalidOperationException>(() => SceneShotDeletion.Delete(updated, "two", revision));
        Assert.Throws<KeyNotFoundException>(() => SceneShotDeletion.Delete(updated, "missing", ScenePromptReducer.Revision(updated)));
        Assert.Throws<ArgumentException>(() => SceneShotDeletion.Delete(w, id, ""));
        Assert.Equal(4, w.Prompts!.Count);
    }

    [Fact]
    public async Task Last_shot_deletion_persists_and_delayed_apply_cannot_resurrect_it()
    {
        var directory = Path.Combine(Path.GetTempPath(), "scene-delete-" + Guid.NewGuid().ToString("N"));
        try {
            var path = Path.Combine(directory, "scene.db");
            var store = new SqliteSceneMemoryStore(path);
            var w = await store.CreateAsync("Story");
            var before = ScenePromptReducer.Revision(w);
            var preview = new ScenePromptPreview("one", before, "A quiet moment.", "One", "test", new([], [], []));
            w = await store.ApplyPromptAsync(w.Id, preview);
            w = await store.DeleteShotAsync(w.Id, "one", ScenePromptReducer.Revision(w));
            var reopened = await new SqliteSceneMemoryStore(path).GetAsync(w.Id);
            Assert.Empty(reopened.Prompts!); Assert.Empty(reopened.History);
            Assert.Equal(new[] { "one" }, reopened.DeletedShotIds);
            Assert.NotEqual(before, ScenePromptReducer.Revision(reopened));
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyPromptAsync(w.Id, preview));
            w = await store.AddEntityAsync(w.Id, "Maya", "Character");
            w = await store.RecordAsync(w.Id, w.Entities.Single().Id, "mood", "Calm", null);
            Assert.Equal(1, Assert.Single(w.History).Sequence);
            w = await store.ApplyPromptAsync(w.Id, preview with { Id = "two", Revision = ScenePromptReducer.Revision(w) });
            Assert.Equal("two", Assert.Single(w.Prompts!).Id);
        } finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}

[Collection("LlmStatic")]
public sealed class SceneShotDeletionHttpTests(SceneStudioWebApplicationFactory factory) : IClassFixture<SceneStudioWebApplicationFactory>
{
    [Fact]
    public async Task Delete_route_checks_revision_removes_shot_and_invalidates_apply_retry()
    {
        using var client = factory.CreateClient();
        async Task<T> Read<T>(HttpResponseMessage r) { Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return (await r.Content.ReadFromJsonAsync<T>())!; }
        var w = await Read<SceneMemoryWorkspaceResponse>(await client.PostAsJsonAsync("/api/scene-memory/v1/workspaces/connect", new ConnectStoryRequest("Delete", "cynelo", Guid.NewGuid().ToString("N"), "story")));
        var path = $"/api/scene-memory/v1/workspaces/{w.Id}";
        w = await Read<SceneMemoryWorkspaceResponse>(await client.PutAsJsonAsync(path + "/studio-entities/maya", new SyncStudioRequest("asset", 1, "Maya", "Character", new())));
        var p = await Read<SceneMemoryPreviewResponse>(await client.PostAsJsonAsync(path + "/previews", new InterpretSceneRequest("Maya holds a mug.", Model: "test")));
        w = await Read<SceneMemoryWorkspaceResponse>(await client.PostAsync(path + $"/previews/{p.Preview.Id}/apply", null));
        Task<HttpResponseMessage> Delete(string id, string rev) => client.SendAsync(new(HttpMethod.Delete, path + "/shots/" + id) { Content = JsonContent.Create(new PrepareSceneVideoRequest(rev)) });
        Assert.Equal(HttpStatusCode.Conflict, (await Delete(p.Preview.Id, "stale")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Delete(p.Preview.Id, "")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Delete("missing", w.Revision)).StatusCode);
        var result = await Read<SceneTimelineResponse>(await Delete(p.Preview.Id, w.Revision));
        Assert.Empty(result.Shots);
        Assert.Equal(w.Entities.Count, result.Entities.Count);
        Assert.NotEqual(w.Revision, result.Revision);
        var context = await Read<SceneContinuityResponse>(await client.GetAsync(path + "/context"));
        Assert.Empty(context.CurrentFacts);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync(path + $"/previews/{p.Preview.Id}/apply", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(path + $"/shots/{p.Preview.Id}/video-prompt", new PrepareSceneVideoRequest(result.Revision))).StatusCode);
    }
}
