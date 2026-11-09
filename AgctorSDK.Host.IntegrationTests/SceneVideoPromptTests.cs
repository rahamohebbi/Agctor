using AgctorSDK.Core.SceneMemory;
using AgctorSDK.Host.Services;
using Xunit;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class SceneVideoPromptTests
{
    private static readonly DateTimeOffset Time = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
    private static SceneWorkspace LegacyStory() => new("story", "Mountain adventure", Time,
        [new("raha", "Raha", "Character"), new("ryan", "Ryan", "Character"),
         new("sword-a", "Raha's sword", "Object", new("sword-a", "design-a", 1, "Golden sword", new Dictionary<string, string> { ["appearance"] = "Golden glow", ["cynelo_version_id"] = "1" })),
         new("sword-b", "Ryan's sword", "Object"), new("monster", "Earth Monster", "Character")],
        [Fact(1, "raha", "status", "trapped", "one"), Fact(2, "ryan", "status", "trapped", "one"),
         Fact(3, "sword-a", "held_by", "Raha", "one", "raha"), Fact(4, "sword-b", "held_by", "Ryan", "one", "ryan"),
         Fact(5, "raha", "status", "free", "two"), Fact(6, "ryan", "status", "free", "two"),
         Fact(7, "monster", "location", "over the mountains", "two")],
        [new("one", "Raha and Ryan are trapped in slime, holding their swords.", "Trapped", "test", 1, 4, Time),
         new("two", "Raha and Ryan cut through the slime with their swords, leap free and raise their swords. The Earth Monster flees over the mountains.", "Fight back", "test", 5, 7, Time)]);
    private static SceneWorkspace Story()
    {
        var w = LegacyStory();
        return w with { Prompts = w.Prompts!.Select(p => p with { DesignSnapshots = SceneShotDesigns.Capture(w.Entities) }).ToArray() };
    }
    [Fact]
    public void Legacy_shot_uses_current_links_without_pinning_or_mutating_history()
    {
        var w = LegacyStory();
        Assert.False(SceneShotDesigns.Review(w, "one").IsPinned);
        Assert.Contains("Golden glow", SceneVideoPromptBuilder.Prepare(w, "one", ScenePromptReducer.Revision(w)).Prompt);
        Assert.Null(w.Prompts!.Single(p => p.Id == "two").DesignSnapshots);
    }

    private static SceneFact Fact(long n, string id, string property, string value, string shot, string? target = null, bool removed = false) =>
        new(n, id, property, value, "source", Time, target, removed, shot);

    [Fact]
    public void Fight_back_preserves_swords_and_distinguishes_start_from_end_without_mutating_story()
    {
        var story = Story(); var revision = ScenePromptReducer.Revision(story);
        var draft = SceneVideoPromptBuilder.Prepare(story, "two", revision);
        Assert.Equal("trapped", draft.StateBefore.Single(f => f.EntityId == "raha").Value);
        Assert.Equal("free", draft.StateAfter.Single(f => f.EntityId == "raha").Value);
        Assert.Equal("free", draft.StateAfter.Single(f => f.EntityId == "ryan").Value);
        Assert.Equal(2, draft.StateAfter.Count(f => f.Property == "held_by"));
        Assert.Contains("Golden glow", draft.Prompt);
        Assert.Contains("START OF SHOT", draft.Prompt); Assert.Contains("END OF SHOT", draft.Prompt);
        Assert.Contains("over the mountains", draft.Prompt);
        Assert.DoesNotContain("cynelo_version_id", draft.Prompt);
        Assert.Equal(revision, ScenePromptReducer.Revision(story));
        Assert.Equal(draft.Prompt, SceneVideoPromptBuilder.Prepare(story, "two", revision).Prompt);
    }

    [Fact]
    public void Earlier_shot_does_not_include_future_state()
    {
        var story = Story(); var draft = SceneVideoPromptBuilder.Prepare(story, "one", ScenePromptReducer.Revision(story));
        Assert.Empty(draft.StateBefore);
        Assert.DoesNotContain(draft.StateAfter, f => f.Value == "free");
        Assert.DoesNotContain("over the mountains", draft.Prompt);
    }

    [Fact]
    public void Cleared_fact_is_explicit_and_does_not_resurrect_possession()
    {
        var story = Story();
        story = story with { History = story.History.Append(Fact(8, "sword-a", "held_by", "", "two", removed: true)).ToArray(),
            Prompts = story.Prompts!.Select(p => p.Id == "two" ? p with { LastSequence = 8 } : p).ToArray() };
        var draft = SceneVideoPromptBuilder.Prepare(story, "two", ScenePromptReducer.Revision(story));
        Assert.DoesNotContain(draft.StateAfter, f => f.EntityId == "sword-a" && f.Property == "held_by");
        Assert.Contains("cleared in this shot (was Raha)", draft.Prompt);
    }

    [Fact]
    public void Context_includes_latest_manual_facts_but_not_removed_facts_or_events()
    {
        var story = Story();
        story = story with { History = story.History.Concat([
            Fact(8, "raha", "mood", "happy", ""), Fact(9, "monster", "location", "", "", removed: true),
            Fact(10, "raha", "action", "smiles", "") with { IsEvent = true }]).ToArray() };
        var context = SceneVideoPromptBuilder.Context(story);
        Assert.Contains(context.CurrentFacts, f => f.Property == "mood" && f.Value == "happy");
        Assert.DoesNotContain(context.CurrentFacts, f => f.Property is "location" or "action");
        Assert.Equal("two", context.PreviousShotId);
    }

    [Fact]
    public void Continuity_only_shot_with_no_named_entities_retains_existing_state()
    {
        var story = Story();
        story = story with { History = story.History.Where(f => f.Sequence <= 4).ToArray(),
            Prompts = story.Prompts!.Select(p => p.Id == "two" ? p with { Prompt = "Hold this pose.", FirstSequence = 5, LastSequence = 4 } : p).ToArray() };
        var draft = SceneVideoPromptBuilder.Prepare(story, "two", ScenePromptReducer.Revision(story));
        Assert.Equal(4, draft.StateBefore.Count);
        Assert.Equal(4, draft.StateAfter.Count);
        Assert.Contains("Golden glow", draft.Prompt);
        Assert.Contains(draft.StateAfter, f => f.EntityId == "ryan" && f.Value == "trapped");
    }

    [Fact]
    public void Unknown_shot_and_stale_reference_revision_are_rejected()
    {
        var story = Story(); var revision = ScenePromptReducer.Revision(story);
        Assert.Throws<KeyNotFoundException>(() => SceneVideoPromptBuilder.Prepare(story, "unapplied", revision));
        var changed = story with { Entities = story.Entities.Select(e => e.Studio == null ? e : e with { Studio = e.Studio with { Version = 2 } }).ToArray() };
        Assert.Throws<InvalidOperationException>(() => SceneVideoPromptBuilder.Prepare(changed, "two", revision));
    }
}
