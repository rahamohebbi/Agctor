using AgctorSDK.Core.SceneMemory;
using Xunit;

namespace AgctorSDK.Core.Tests;

public sealed class SceneStateTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "scene-states-" + Guid.NewGuid().ToString("N"));
    private SqliteSceneMemoryStore Store() => new(Path.Combine(_directory, "scene.db"));
    private static ScenePromptPreview Preview(SceneWorkspace workspace, string prompt, ScenePromptChange[] changes,
        ScenePromptEvent[]? events = null) => new(Guid.NewGuid().ToString("N"), ScenePromptReducer.Revision(workspace), prompt, "", "test",
        new(Array.Empty<ScenePromptEntity>(), changes, Array.Empty<string>(), events));
    private async Task<SceneWorkspace> Setup()
    {
        var store = Store(); var w = await store.CreateAsync("Flexible story");
        foreach (var (name, kind) in new[] { ("ActorA", "Character"), ("ActorB", "Character"), ("ActorC", "Character"), ("Pen", "Object"), ("Garden", "Location") })
            w = await store.AddEntityAsync(w.Id, name, kind);
        return w;
    }

    [Theory]
    [InlineData("discarded")]
    [InlineData("lost")]
    public async Task Disposition_ends_carrying_without_destruction_and_pickup_recovers(string disposition)
    {
        var store = Store(); var w = await Setup(); var a = w.Entities[0]; var b = w.Entities[1]; var pen = w.Entities[3];
        w = await store.SetHolderAsync(w.Id, pen.Id, a.Id, null);
        w = await store.RecordAsync(w.Id, pen.Id, "carried_at", "pocket", null);
        var before = w.History.Count;
        w = await store.ApplyPromptAsync(w.Id, Preview(w, disposition, new[] { new ScenePromptChange(pen.Id, "status", disposition, "", false, disposition) }));
        var result = await store.QueryAsync(w.Id, new(pen.Id));
        Assert.Contains(result.Facts, f => f.Property == "disposition" && f.Value == disposition);
        Assert.DoesNotContain(result.Facts, f => f.Property is "held_by" or "carried_at" or "status");
        Assert.DoesNotContain((await store.QueryAsync(w.Id, new(a.Id))).Facts, f => f.Property == "holding");
        Assert.Contains((await store.QueryAsync(w.Id, new(pen.Id, AtSequence: before))).Facts, f => f.Property == "carried_at");
        w = await store.SetHolderAsync(w.Id, pen.Id, b.Id, "Found it");
        result = await Store().QueryAsync(w.Id, new(pen.Id));
        Assert.Contains(result.Facts, f => f.Property == "disposition" && f.Value == "recovered");
        Assert.Contains(result.Facts, f => f.Property == "held_by" && f.Value == b.Name);
    }

    [Fact]
    public async Task Death_ends_activity_but_preserves_identity_abilities_possession_and_allows_explicit_revival()
    {
        var store = Store(); var w = await Setup(); var a = w.Entities[0]; var pen = w.Entities[3];
        w = await store.SetHolderAsync(w.Id, pen.Id, a.Id, null);
        w = await store.RecordAsync(w.Id, a.Id, "ability_sing", "true", null);
        w = await store.RecordAsync(w.Id, a.Id, "activity", "walking", null);
        w = await store.ApplyPromptAsync(w.Id, Preview(w, "ActorA dies", new[] { new ScenePromptChange(a.Id, "status", "dead", "", false, "dies") }));
        var result = await store.QueryAsync(w.Id, new(a.Id));
        Assert.Contains(result.Facts, f => f.Property == "life_state" && f.Value == "dead");
        Assert.Contains(result.Facts, f => f.Property == "ability_sing");
        Assert.Contains(result.Facts, f => f.Property == "holding");
        Assert.DoesNotContain(result.Facts, f => f.Property == "activity");
        await Assert.ThrowsAnyAsync<ArgumentException>(() => store.ApplyPromptAsync(w.Id, Preview(w, "walks", new[] {
            new ScenePromptChange(a.Id, "activity", "walking", "", false, "walks") })));
        w = await store.ApplyPromptAsync(w.Id, Preview(w, "ActorA revives and walks", new[] {
            new ScenePromptChange(a.Id, "life_state", "alive", "", false, "revives"),
            new ScenePromptChange(a.Id, "activity", "walking", "", false, "walks") }));
        Assert.Contains((await store.QueryAsync(w.Id, new(a.Id))).Facts, f => f.Property == "activity" && f.Value == "walking");
    }

    [Fact]
    public async Task Skills_health_availability_and_emotion_are_independent_and_reversible()
    {
        var store = Store(); var w = await Setup(); var c = w.Entities[2];
        const string prompt = "ActorC can sing and dance but got sick and is too sick to sing today. ActorC feels sad.";
        w = await store.ApplyPromptAsync(w.Id, Preview(w, prompt, new[] {
            new ScenePromptChange(c.Id, "can_sing", "true", "", false, "can sing"),
            new ScenePromptChange(c.Id, "ability_dance", "true", "", false, "dance"),
            new ScenePromptChange(c.Id, "condition", "sick", "", false, "got sick"),
            new ScenePromptChange(c.Id, "availability_sing", "unable", "", false, "too sick to sing today"),
            new ScenePromptChange(c.Id, "emotion", "sad", "", false, "feels sad") }));
        var before = w.History.Count;
        w = await store.ApplyPromptAsync(w.Id, Preview(w, "ActorC recovers and can sing again", new[] {
            new ScenePromptChange(c.Id, "health", "healthy", "", false, "recovers"),
            new ScenePromptChange(c.Id, "availability_sing", "available", "", false, "can sing again") }));
        var result = await store.QueryAsync(w.Id, new(c.Id));
        Assert.Equal(2, result.Facts.Count(f => f.Property.StartsWith("ability_")));
        Assert.Contains(result.Facts, f => f.Property == "health" && f.Value == "healthy");
        Assert.Contains(result.Facts, f => f.Property == "emotion" && f.Value == "sad");
        Assert.Contains(result.Facts, f => f.Property == "availability_sing" && f.Value == "available");
        Assert.Contains((await store.QueryAsync(w.Id, new(c.Id, AtSequence: before))).Facts, f => f.Property == "health" && f.Value == "sick");
    }

    [Fact]
    public async Task Location_damage_does_not_invent_death_and_open_attributes_and_relations_survive()
    {
        var store = Store(); var w = await Setup(); var a = w.Entities[0]; var garden = w.Entities[4];
        const string prompt = "ActorA is in the garden and trusts ActorB. The garden is rainy. The garden is destroyed.";
        w = await store.ApplyPromptAsync(w.Id, Preview(w, prompt, new[] {
            new ScenePromptChange(a.Id, "located_at", "", garden.Id, false, "ActorA is in the garden"),
            new ScenePromptChange(a.Id, "trusts", "", w.Entities[1].Id, false, "trusts ActorB"),
            new ScenePromptChange(garden.Id, "weather", "rainy", "", false, "rainy"),
            new ScenePromptChange(garden.Id, "status", "destroyed", "", false, "destroyed") }));
        var result = await store.QueryAsync(w.Id, new(a.Id));
        Assert.DoesNotContain(result.Facts, f => f.Property == "life_state");
        Assert.Contains(result.Facts, f => f.Property == "located_at");
        Assert.Contains(result.Facts, f => f.Property == "trusts" && f.RelatedEntityId == w.Entities[1].Id);
        Assert.Contains((await store.QueryAsync(w.Id, new(garden.Id))).Facts, f => f.Property == "weather");
    }

    [Theory]
    [InlineData("occurred")]
    [InlineData("ongoing")]
    [InlineData("intended")]
    [InlineData("hypothetical")]
    [InlineData("negated")]
    public async Task Repeated_events_are_retrievable_from_all_participants_and_never_overwrite_current_state(string mode)
    {
        var store = Store(); var w = await Setup(); var a = w.Entities[0]; var pen = w.Entities[3];
        w = await store.SetHolderAsync(w.Id, pen.Id, a.Id, null);
        var oldSequence = w.History.Count;
        const string prompt = "ActorA mentions the pen.";
        for (int i = 0; i < 2; i++)
            w = await store.ApplyPromptAsync(w.Id, Preview(w, prompt, Array.Empty<ScenePromptChange>(),
                new[] { new ScenePromptEvent(prompt, new[] { a.Id, pen.Id }, prompt, mode) }));
        foreach (var id in new[] { a.Id, pen.Id })
        {
            var result = await Store().QueryAsync(w.Id, new(id));
            Assert.Equal(2, result.Events!.Count);
            Assert.All(result.Events, e => Assert.Equal(mode, e.EventMode));
            Assert.DoesNotContain(result.Facts, f => f.IsEvent);
            Assert.Contains(result.Facts, f => f.Property is "holding" or "held_by");
            Assert.Empty((await store.QueryAsync(w.Id, new(id, AtSequence: oldSequence))).Events!);
        }
    }

    [Fact]
    public async Task Invalid_event_rolls_back_prior_state_and_manual_death_disposal_use_same_rules()
    {
        var store = Store(); var w = await Setup(); var a = w.Entities[0]; var pen = w.Entities[3];
        await Assert.ThrowsAnyAsync<ArgumentException>(() => store.ApplyPromptAsync(w.Id, Preview(w, "ActorA smiles", new[] {
            new ScenePromptChange(a.Id, "emotion", "happy", "", false, "smiles") },
            new[] { new ScenePromptEvent("Smiles", new[] { "unknown" }, "smiles") })));
        Assert.Empty((await store.GetAsync(w.Id)).History);
        w = await store.RecordAsync(w.Id, a.Id, "activity", "walking", null);
        w = await store.RecordAsync(w.Id, a.Id, "status", "dead", null);
        Assert.DoesNotContain((await store.QueryAsync(w.Id, new(a.Id))).Facts, f => f.Property == "activity");
        w = await store.SetHolderAsync(w.Id, pen.Id, a.Id, null);
        await store.RecordAsync(w.Id, pen.Id, "status", "discarded", null);
        Assert.DoesNotContain((await store.QueryAsync(w.Id, new(pen.Id))).Facts, f => f.Property == "held_by");
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
