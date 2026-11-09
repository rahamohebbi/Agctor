using System.Text.Json;
using AgctorSDK.Core.SceneMemory;
using AgctorSDK.Host.Services;
using AgctorSDK.Host.Services.VideoWorkflow;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class SceneShotParticipantIsolationTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
    private static SceneEntity Linked(string id, string name, string kind) => new(id, name, kind,
        new(id, id, 1, name, new Dictionary<string, string> { ["cynelo_reference_image_1"] = id + ".png", ["appearance"] = name + " design" }));
    private static SceneFact Fact(long sequence, string entity, string property, string value, string shot, string? target = null) =>
        new(sequence, entity, property, value, "source", At, target, PromptId: shot);
    private static SceneWorkspace Forest()
    {
        SceneEntity[] entities = [Linked("raha", "Raha", "Character"), Linked("ryan", "Ryan", "Character"),
            Linked("sword", "Sword", "Object"), Linked("snake", "The Acid Snake", "Character"), Linked("dragon", "Magical Dragon", "Object"),
            new("woman", "Woman in red coat", "Character"), new("forest", "Quiet forest", "Location"), new("bridge", "Wooden bridge", "Location")];
        return new("story", "Existing story", At, entities, [
            Fact(1, "raha", "species", "chipmunk", "old"), Fact(2, "ryan", "species", "chipmunk", "old"),
            Fact(3, "sword", "held_by", "Ryan", "old", "ryan"), Fact(4, "ryan", "located_at", "Quiet forest", "old", "forest"),
            Fact(5, "woman", "clothing_color", "red", "one"), Fact(6, "woman", "located_at", "Quiet forest", "one", "forest"),
            Fact(7, "woman", "located_at", "Wooden bridge", "two", "bridge")], [
            new("old", "Raha and Ryan talk. Ryan holds a sword.", "", "test", 1, 4, At, "Old adventure", DesignSnapshots: entities),
            new("one", "A woman in a red coat walks through a quiet forest at sunrise.", "Clip 1", "test", 5, 6, At,
                "Video sequence", IntroducedEntityIds: ["woman", "forest"], DesignSnapshots: entities),
            new("two", "She reaches the wooden bridge, stops halfway and looks down at the river.", "Clip 2", "test", 7, 7, At,
                "Video sequence", DesignSnapshots: entities)]);
    }

    [Theory] [InlineData("one")] [InlineData("two")]
    public void Forest_shots_exclude_unrequested_library_images_text_and_facts(string shot)
    {
        var workspace = Forest(); var unchanged = JsonSerializer.Serialize(workspace);
        var draft = SceneVideoPromptBuilder.Prepare(workspace, shot, ScenePromptReducer.Revision(workspace));
        Assert.Contains(draft.Entities, e => e.Id == "woman");
        Assert.Empty(draft.Entities.Where(e => e.Studio != null));
        foreach (var unexpected in new[] { "Raha", "Ryan", "Sword", "The Acid Snake", "Magical Dragon", "chipmunk" })
            Assert.DoesNotContain(unexpected, draft.Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(draft.StateBefore.Concat(draft.StateAfter), f => f.EntityId is "raha" or "ryan" or "sword");
        Assert.Equal(unchanged, JsonSerializer.Serialize(workspace));
        var timeline = SceneMemoryApiProjection.Timeline(workspace);
        Assert.DoesNotContain(timeline.Shots.Single(s => s.Id == shot).Participants!, e => e.Studio != null);
    }
    [Fact]
    public void Replayed_old_facts_and_dialogue_do_not_expand_the_new_shot_cast()
    {
        var workspace = Forest();
        var oldEvent = Fact(0, "raha", "event", "Raha compliments Ryan.", "old") with { IsEvent = true, ParticipantIds = ["raha", "ryan"], EventMode = "occurred" };
        workspace = workspace with {
            History = workspace.History.Prepend(oldEvent).Concat([
                Fact(8, "raha", "species", "chipmunk", "two"), Fact(9, "ryan", "species", "chipmunk", "two"),
                oldEvent with { Sequence = 10, PromptId = "two", Note = "She reaches the bridge." }]).ToArray(),
            Prompts = workspace.Prompts!.Select(p => p.Id == "two" ? p with { LastSequence = 10 } : p).ToArray()
        };
        var draft = SceneVideoPromptBuilder.Prepare(workspace, "two", ScenePromptReducer.Revision(workspace));
        Assert.DoesNotContain(draft.Entities, e => e.Id is "raha" or "ryan" or "sword");
        Assert.DoesNotContain("chipmunk", draft.Prompt);
        Assert.Contains(draft.Entities, e => e.Id == "woman");
    }
    [Fact]
    public void New_story_scope_does_not_treat_style_consistency_as_continuing_the_old_story()
    {
        var workspace = Forest();
        workspace = workspace with { Entities = workspace.Entities.Where(e => e.Studio != null).ToArray(),
            History = workspace.History.Where(f => f.Sequence <= 4).ToArray(), Prompts = workspace.Prompts!.Take(1).ToArray() };
        var state = new SceneVideoState { Input = new("request", workspace.Id,
            "A woman in a red coat walks through a quiet forest. Keep the same character, outfit, lighting and location throughout.") };
        var scoped = SceneVideoMemoryScope.Select(workspace, state);
        Assert.Empty(scoped.Entities); Assert.Empty(scoped.History); Assert.Empty(scoped.Prompts!);
    }
    [Fact]
    public void Scope_keeps_same_woman_across_clips_and_rejects_hidden_library_references()
    {
        var workspace = Forest();
        var state = new SceneVideoState { Input = new("request", workspace.Id, "A woman in a red coat walks to a wooden bridge."),
            Current = 1, Clips = [new() { ShotId = "one" }, new() { ShotId = "new-clip" }] };
        var scoped = SceneVideoMemoryScope.Select(workspace, state);
        Assert.Contains(scoped.Entities, e => e.Id == "woman");
        Assert.DoesNotContain(scoped.Entities, e => e.Studio != null);
        Assert.DoesNotContain(scoped.History, f => f.EntityId is "raha" or "ryan" or "sword");
        var preview = new ScenePromptPreview("new-clip", ScenePromptReducer.Revision(workspace), "She continues walking.", "", "test",
            new([], [new("ryan", "mood", "happy", "", false, "She continues walking.")], []));
        Assert.Throws<ScenePromptValidationException>(() => SceneVideoMemoryScope.Validate(workspace, scoped, preview));
        preview = preview with { Extraction = new([new("new-ryan", "Ryan", "Character")], [], []) };
        Assert.Throws<InvalidOperationException>(() => SceneVideoMemoryScope.Validate(workspace, scoped, preview));
    }
    [Fact]
    public void Hold_pose_inherits_only_the_preceding_shots_participants()
    {
        var workspace = Forest();
        workspace = workspace with { Prompts = workspace.Prompts!.Append(new("three", "Hold this pose.", "Clip 3", "test", 8, 7, At,
            "Video sequence", DesignSnapshots: workspace.Entities)).ToArray() };
        var participants = SceneVideoPromptBuilder.Participants(workspace, "three");
        Assert.Contains(participants, e => e.Id == "woman");
        Assert.DoesNotContain(participants, e => e.Studio != null);
    }
    [Theory] [InlineData("A new landscape at sunset.", "Video sequence")] [InlineData("Hold this pose.", "Different scene")]
    public void Unresolved_new_action_or_scene_boundary_does_not_import_previous_cast(string prompt, string scene)
    {
        var workspace = Forest();
        workspace = workspace with { Prompts = workspace.Prompts!.Append(new("three", prompt, "", "test", 8, 7, At,
            scene, DesignSnapshots: workspace.Entities)).ToArray() };
        Assert.Empty(SceneVideoPromptBuilder.Participants(workspace, "three"));
    }
    [Fact]
    public void Social_relationships_do_not_add_unrequested_characters_or_their_props()
    {
        var workspace = Forest();
        workspace = workspace with { History = workspace.History.Append(Fact(8, "woman", "friend_of", "Raha", "old", "raha")).ToArray() };
        var third = new ScenePromptRecord("three", "The woman in red coat smiles.", "", "test", 9, 8, At,
            "Video sequence", DesignSnapshots: workspace.Entities);
        workspace = workspace with { Prompts = workspace.Prompts!.Append(third).ToArray() };
        var draft = SceneVideoPromptBuilder.Prepare(workspace, "three", ScenePromptReducer.Revision(workspace));
        Assert.DoesNotContain("Raha", draft.Prompt);
        Assert.DoesNotContain(draft.Entities, e => e.Id is "raha" or "ryan" or "sword");
    }
}
