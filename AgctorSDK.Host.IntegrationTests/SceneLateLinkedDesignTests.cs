using AgctorSDK.Core.SceneMemory;
using AgctorSDK.Host.Services;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class SceneLateLinkedDesignTests
{
    [Theory]
    [InlineData("Sword", "Object", "Raha and Ryan draw their matching glowing swords.")]
    [InlineData("Box", "Object", "Raha opens the boxes.")]
    [InlineData("Observatory", "Location", "Raha enters the observatories.")]
    [InlineData("Captain", "Character", "Raha greets the Captain.")]
    public async Task A_later_link_named_in_saved_action_is_included_without_rewriting_history(string name, string kind, string prompt)
    {
        var directory = Path.Combine(Path.GetTempPath(), "late-scene-design-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(directory, "scene.db");
            var store = new SqliteSceneMemoryStore(path);
            var w = await store.ConnectAsync("Story", new("cynelo", "project", "story"));
            w = await store.SyncStudioAsync(w.Id, "raha", new("raha", 1, "Raha", "Character", new Dictionary<string, string>()));
            var actor = w.Entities.Single();
            w = await store.ApplyPromptAsync(w.Id, new("one", ScenePromptReducer.Revision(w), prompt, "Shot", "test",
                new([], [], [], [new(prompt, [actor.Id], prompt)])));
            var snapshot = w.Prompts![0];
            var history = w.History.ToArray();
            w = await store.SyncStudioAsync(w.Id, "late-design", new("design", 1, name, kind,
                new Dictionary<string, string> { ["description"] = "Latest design", ["cynelo_reference_image_1"] = "sword-photo.png" }));
            w = await store.SyncStudioAsync(w.Id, "unrelated", new("unrelated", 1, "Spaceship", "Object", new Dictionary<string, string>()));
            w = await new SqliteSceneMemoryStore(path).GetAsync(w.Id);
            var revision = ScenePromptReducer.Revision(w);
            var draft = SceneVideoPromptBuilder.Prepare(w, "one", revision);
            var included = Assert.Single(draft.Entities.Where(e => e.Studio?.AssetId == "design"));
            Assert.Equal("sword-photo.png", included.Studio!.Defaults["cynelo_reference_image_1"]);
            Assert.DoesNotContain(draft.Entities, e => e.Studio?.AssetId == "unrelated");
            Assert.Contains("Latest design", draft.Prompt);
            Assert.Contains(SceneMemoryApiProjection.Timeline(w).Shots[0].Participants!, e => e.Id == included.Id);
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(history), System.Text.Json.JsonSerializer.Serialize(w.History));
            Assert.Equal(snapshot.Prompt, w.Prompts![0].Prompt);
            Assert.Single(w.Prompts[0].DesignSnapshots!);
            Assert.Equal(revision, ScenePromptReducer.Revision(w));
            Assert.DoesNotContain(draft.StateAfter, f => f.EntityId == included.Id);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("Maya watches a swordfish.")]
    [InlineData("Maya smiles.")]
    public void Story_links_without_a_matching_script_mention_are_not_shot_participants(string prompt)
    {
        var now = DateTimeOffset.UtcNow;
        var actor = new SceneEntity("maya", "Maya", "Character");
        var sword = new SceneEntity("sword", "Sword", "Object", new("sword", "sword", 1, "Sword", new Dictionary<string, string>()));
        var w = new SceneWorkspace("w", "Story", now, [actor, sword], [],
            [new("one", prompt, "", "test", 1, 0, now, DesignSnapshots: [actor])]);
        Assert.DoesNotContain(SceneVideoPromptBuilder.Participants(w, "one"), e => e.Id == sword.Id);
        w = w with { Entities = [actor, sword with { Studio = null }] };
        Assert.DoesNotContain(SceneVideoPromptBuilder.Participants(w, "one"), e => e.Id == sword.Id);
    }

    [Fact]
    public void Explicitly_named_late_design_does_not_import_future_shot_facts()
    {
        var now = DateTimeOffset.UtcNow;
        var actor = new SceneEntity("maya", "Maya", "Character");
        var future = new SceneEntity("future", "Future sword", "Object", new("future", "future", 1, "Sword", new Dictionary<string, string>()));
        var w = new SceneWorkspace("w", "Story", now, [actor, future], [], [
            new("one", "Maya holds a sword.", "", "test", 1, 0, now, DesignSnapshots: [actor]),
            new("two", "A different sword appears.", "", "test", 1, 0, now, IntroducedEntityIds: [future.Id], DesignSnapshots: [actor, future])]);
        w = w with { History = [new(1, future.Id, "color", "red", "later", now, PromptId: "two")] };
        var draft = SceneVideoPromptBuilder.Prepare(w, "one", ScenePromptReducer.Revision(w));
        Assert.Contains(draft.Entities, e => e.Id == future.Id);
        Assert.Empty(draft.StateAfter);
        Assert.Empty(draft.StateBefore);
    }
}
