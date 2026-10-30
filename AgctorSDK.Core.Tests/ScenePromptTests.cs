using AgctorSDK.Core.SceneMemory;
using Xunit;

namespace AgctorSDK.Core.Tests;

public sealed class ScenePromptTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "scene-prompts-" + Guid.NewGuid().ToString("N"));
    private SqliteSceneMemoryStore Store() => new(Path.Combine(_directory, "scene.db"));
    private const string Prompt = "Maya carries a red mug. She places it on the counter.";
    private static ScenePromptPreview Plan(SceneWorkspace workspace) => new(Guid.NewGuid().ToString("N"), ScenePromptReducer.Revision(workspace),
        Prompt, "Shot 1", "test-model", new ScenePromptExtraction(
            new[] { new ScenePromptEntity("maya", "Maya", "Character"), new ScenePromptEntity("mug", "Red mug", "Object"), new ScenePromptEntity("counter", "Counter", "Object") },
            new[] { new ScenePromptChange("mug", "color", "red", "", false, "red mug"),
                new ScenePromptChange("mug", "held_by", "", "maya", false, "Maya carries a red mug"),
                new ScenePromptChange("mug", "on", "", "counter", false, "She places it on the counter") }, Array.Empty<string>()));

    [Fact]
    public async Task Prompt_discovers_entities_preserves_intermediate_state_and_reuses_identity_across_prompts()
    {
        var store = Store(); var workspace = await store.CreateAsync("Prompt story");
        // A pre-existing Studio/manual identity must survive discovery under a local model reference.
        workspace = await store.AddEntityAsync(workspace.Id, "Maya", "Character");
        var mayaId = workspace.Entities.Single().Id;
        var preview = Plan(workspace);
        workspace = await store.ApplyPromptAsync(workspace.Id, preview);
        Assert.Equal(3, workspace.Entities.Count);
        Assert.Equal(mayaId, workspace.Entities.Single(e => e.Name == "Maya").Id);
        var mug = workspace.Entities.Single(e => e.Name == "Red mug");
        var current = await Store().QueryAsync(workspace.Id, new(mug.Id));
        Assert.DoesNotContain(current.Facts, f => f.Property == "held_by");
        Assert.Contains(current.Facts, f => f.Property == "on" && f.Value == "Counter");
        Assert.Contains((await store.QueryAsync(workspace.Id, new(mug.Id, AtSequence: 2))).Facts, f => f.Property == "held_by" && f.Value == "Maya");
        Assert.DoesNotContain((await store.QueryAsync(workspace.Id, new(mayaId))).Facts, f => f.Property == "holding");
        Assert.All(workspace.History, f => Assert.Equal(preview.Id, f.PromptId));
        Assert.Equal(Prompt, Assert.Single((await Store().GetAsync(workspace.Id)).Prompts!).Prompt);
        // Retrying after a lost response does not duplicate the prompt or its facts.
        Assert.Equal(workspace.History.Count, (await store.ApplyPromptAsync(workspace.Id, preview)).History.Count);
        var next = new ScenePromptPreview(Guid.NewGuid().ToString("N"), ScenePromptReducer.Revision(workspace), "Alex picks up the mug.", "Shot 2", "test",
            new(new[] { new ScenePromptEntity("alex", "Alex", "Character") },
                new[] { new ScenePromptChange(mug.Id, "held_by", "", "alex", false, "Alex picks up the mug") }, Array.Empty<string>()));
        workspace = await store.ApplyPromptAsync(workspace.Id, next);
        Assert.Equal(4, workspace.Entities.Count);
        current = await store.QueryAsync(workspace.Id, new(mug.Id));
        Assert.DoesNotContain(current.Facts, f => f.Property == "on");
        Assert.Contains(current.Facts, f => f.Property == "held_by" && f.Value == "Alex");
        Assert.Contains((await store.QueryAsync(workspace.Id, new(workspace.Entities.Single(e => e.Name == "Alex").Id))).Facts,
            f => f.Property == "holding" && f.RelatedEntityId == mug.Id);
    }

    [Fact]
    public async Task Invalid_late_change_rolls_back_entities_and_all_earlier_changes()
    {
        var store = Store(); var workspace = await store.CreateAsync("Atomic"); var plan = Plan(workspace);
        plan = plan with { Extraction = plan.Extraction with { Changes = plan.Extraction.Changes.Append(
            new ScenePromptChange("mug", "held_by", "", "foreign-workspace-id", false, "red mug")).ToArray() } };
        await Assert.ThrowsAnyAsync<ArgumentException>(() => store.ApplyPromptAsync(workspace.Id, plan));
        var after = await store.GetAsync(workspace.Id);
        Assert.Empty(after.Entities); Assert.Empty(after.History); Assert.Null(after.Prompts);
    }

    [Fact]
    public async Task Stale_previews_and_ambiguous_prompts_cannot_commit()
    {
        var store = Store(); var workspace = await store.CreateAsync("Conflict"); var plan = Plan(workspace);
        await store.AddEntityAsync(workspace.Id, "Alex", "Character");
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyPromptAsync(workspace.Id, plan));
        workspace = await store.GetAsync(workspace.Id); plan = Plan(workspace);
        plan = plan with { Extraction = plan.Extraction with { Questions = new[] { "Which mug?" } } };
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyPromptAsync(workspace.Id, plan));
        Assert.Single((await store.GetAsync(workspace.Id)).Entities);
        Assert.Empty((await store.GetAsync(workspace.Id)).History);
    }

    [Fact]
    public async Task Uncited_changes_and_circular_placement_are_rejected()
    {
        var store = Store(); var workspace = await store.CreateAsync("Validation"); var plan = Plan(workspace);
        var invalid = plan with { Extraction = plan.Extraction with { Changes = new[] {
            new ScenePromptChange("mug", "color", "blue", "", false, "invented source") } } };
        await Assert.ThrowsAnyAsync<ArgumentException>(() => store.ApplyPromptAsync(workspace.Id, invalid));
        invalid = plan with { Extraction = plan.Extraction with { Changes = plan.Extraction.Changes.Append(
            new ScenePromptChange("counter", "on", "", "mug", false, "counter")).ToArray() } };
        await Assert.ThrowsAnyAsync<ArgumentException>(() => store.ApplyPromptAsync(workspace.Id, invalid));
        Assert.Empty((await store.GetAsync(workspace.Id)).Entities);
    }

    [Fact]
    public async Task Explicit_release_and_manual_pickup_clear_previous_physical_state()
    {
        var store = Store(); var workspace = await store.CreateAsync("Release");
        workspace = await store.ApplyPromptAsync(workspace.Id, Plan(workspace));
        var mug = workspace.Entities.Single(e => e.Name == "Red mug");
        var maya = workspace.Entities.Single(e => e.Name == "Maya");
        workspace = await store.SetHolderAsync(workspace.Id, mug.Id, maya.Id, "Pickup");
        Assert.DoesNotContain((await store.QueryAsync(workspace.Id, new(mug.Id))).Facts, f => f.Property == "on");
        var release = new ScenePromptPreview(Guid.NewGuid().ToString("N"), ScenePromptReducer.Revision(workspace), "Maya drops the mug.", "", "test",
            new(Array.Empty<ScenePromptEntity>(), new[] { new ScenePromptChange(mug.Id, "held_by", "Maya", maya.Id, true, "drops the mug") }, Array.Empty<string>()));
        await store.ApplyPromptAsync(workspace.Id, release);
        Assert.DoesNotContain((await store.QueryAsync(workspace.Id, new(maya.Id))).Facts, f => f.Property == "holding");
        Assert.Contains((await store.QueryAsync(workspace.Id, new(mug.Id, AtSequence: workspace.History.Count))).Facts, f => f.Property == "held_by");
    }

    [Fact]
    public async Task Eating_updates_bread_and_actor_clears_legacy_action_and_keeps_prior_possession_in_history()
    {
        var store = Store(); var workspace = await store.CreateAsync("Bread regression");
        workspace = await store.AddEntityAsync(workspace.Id, "ActorA", "Character");
        workspace = await store.AddEntityAsync(workspace.Id, "Bread", "Object");
        var actor = workspace.Entities[0]; var bread = workspace.Entities[1];
        workspace = await store.SetHolderAsync(workspace.Id, bread.Id, actor.Id, "Received bread");
        workspace = await store.RecordAsync(workspace.Id, actor.Id, "action", "Bread", "ActorA eats the bread");
        var preview = new ScenePromptPreview(Guid.NewGuid().ToString("N"), ScenePromptReducer.Revision(workspace),
            "ActorA eats the bread", "", "test", new(Array.Empty<ScenePromptEntity>(), new[] {
                new ScenePromptChange(bread.Id, "status", "consumed", "", false, "eats the bread"),
                new ScenePromptChange(bread.Id, "consumed_by", actor.Id, "", false, "ActorA eats the bread") }, Array.Empty<string>()));
        workspace = await store.ApplyPromptAsync(workspace.Id, preview);
        var result = await Store().QueryAsync(workspace.Id, new(bread.Id));
        Assert.Contains(result.Facts, f => f.Property == "status" && f.Value == "consumed");
        Assert.Contains(result.Facts, f => f.Property == "consumed_by" && f.Value == "ActorA");
        Assert.DoesNotContain(result.Facts, f => f.Property == "held_by");
        var actorResult = await store.QueryAsync(workspace.Id, new(actor.Id));
        Assert.DoesNotContain(actorResult.Facts, f => f.Property is "holding" or "action");
        Assert.Contains(actorResult.Facts, f => f.Property == "consumed" && f.Value == "Bread");
        Assert.Contains((await store.QueryAsync(workspace.Id, new(bread.Id, AtSequence: 1))).Facts, f => f.Property == "held_by");
        await Assert.ThrowsAnyAsync<ArgumentException>(() => store.SetHolderAsync(workspace.Id, bread.Id, actor.Id, null));
        var pickup = preview with { Id = Guid.NewGuid().ToString("N"), Revision = ScenePromptReducer.Revision(workspace),
            Extraction = new(Array.Empty<ScenePromptEntity>(), new[] { new ScenePromptChange(bread.Id, "held_by", "", actor.Id, false, "the bread") }, Array.Empty<string>()) };
        await Assert.ThrowsAnyAsync<ArgumentException>(() => store.ApplyPromptAsync(workspace.Id, pickup));
    }

    [Theory]
    [InlineData("partially eaten")]
    [InlineData("being eaten")]
    [InlineData("broken")]
    public async Task Nonterminal_condition_does_not_remove_possession(string condition)
    {
        var store = Store(); var workspace = await store.CreateAsync("Partial change");
        workspace = await store.AddEntityAsync(workspace.Id, "ActorA", "Character");
        workspace = await store.AddEntityAsync(workspace.Id, "Object", "Object");
        var item = workspace.Entities[1];
        workspace = await store.SetHolderAsync(workspace.Id, item.Id, workspace.Entities[0].Id, null);
        var preview = new ScenePromptPreview(Guid.NewGuid().ToString("N"), ScenePromptReducer.Revision(workspace), condition, "", "test",
            new(Array.Empty<ScenePromptEntity>(), new[] { new ScenePromptChange(item.Id, "condition", condition, "", false, condition) }, Array.Empty<string>()));
        await store.ApplyPromptAsync(workspace.Id, preview);
        Assert.Contains((await store.QueryAsync(workspace.Id, new(item.Id))).Facts, f => f.Property == "held_by");
    }

    [Theory]
    [InlineData("moves protectively beside Ryan", "Character", "Ryan")]
    [InlineData("tries to block the slime", "Object", "Slime")]
    public async Task Descriptive_action_preserves_verb_and_participant_without_inventing_effects(string action, string kind, string name)
    {
        var store = Store(); var workspace = await store.CreateAsync("Descriptive action");
        workspace = await store.AddEntityAsync(workspace.Id, "Raha", "Character");
        workspace = await store.AddEntityAsync(workspace.Id, name, kind);
        var actor = workspace.Entities[0]; var target = workspace.Entities[1];
        var prompt = "Raha " + action;
        var preview = new ScenePromptPreview(Guid.NewGuid().ToString("N"), ScenePromptReducer.Revision(workspace), prompt, "", "test",
            new(Array.Empty<ScenePromptEntity>(), new[] { new ScenePromptChange(actor.Id, "action", action, target.Id, false, prompt) }, Array.Empty<string>()));
        await store.ApplyPromptAsync(workspace.Id, preview);
        var fact = Assert.Single((await store.QueryAsync(workspace.Id, new(actor.Id))).Facts);
        Assert.Equal("action", fact.Property);
        Assert.Equal(action, fact.Value);
        Assert.Equal(target.Id, fact.RelatedEntityId);
        Assert.Empty((await store.QueryAsync(workspace.Id, new(target.Id))).Facts);
        Assert.Single((await store.GetAsync(workspace.Id)).History);
    }

    [Fact]
    public async Task Matching_props_with_distinct_refs_remain_separate_and_targeted_dialogue_is_preserved()
    {
        var store = Store(); var workspace = await store.CreateAsync("Matching swords");
        const string prompt = "Raha and Ryan draw matching glowing swords. Raha says to Ryan: We fight together!";
        var preview = new ScenePromptPreview(Guid.NewGuid().ToString("N"), ScenePromptReducer.Revision(workspace), prompt, "", "test",
            new(new[] { new ScenePromptEntity("raha", "Raha", "Character"), new("ryan", "Ryan", "Character"),
                new("sword_a", "glowing sword", "Object"), new("sword_b", "glowing sword", "Object") },
                new[] { new ScenePromptChange("sword_a", "held_by", "", "raha", false, "draw matching glowing swords"),
                    new("sword_b", "held_by", "", "ryan", false, "draw matching glowing swords"),
                    new("raha", "speech", "We fight together!", "ryan", false, "Raha says to Ryan: We fight together!") }, Array.Empty<string>()));
        await store.ApplyPromptAsync(workspace.Id, preview);
        var saved = await store.GetAsync(workspace.Id);
        var swords = saved.Entities.Where(e => e.Kind == "Object").ToArray();
        Assert.Equal(2, swords.Length);
        Assert.Equal(2, swords.Select(e => e.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var holders = saved.History.Where(f => f.Property == "held_by").ToArray();
        Assert.Equal(2, holders.Select(f => f.EntityId).Distinct().Count());
        Assert.Equal(2, holders.Select(f => f.RelatedEntityId).Distinct().Count());
        var speech = Assert.Single(saved.History.Where(f => f.Property == "speech"));
        Assert.Equal("We fight together!", speech.Value);
        Assert.Equal(saved.Entities.Single(e => e.Name == "Ryan").Id, speech.RelatedEntityId);

        // A shared display name cannot choose which prop a relationship refers to.
        var ambiguous = preview with { Id = "ambiguous", Extraction = preview.Extraction with {
            Changes = new[] { new ScenePromptChange("raha", "facing", "", "glowing sword", false, "Raha") } } };
        Assert.ThrowsAny<ArgumentException>(() => ScenePromptReducer.Apply(workspace, ambiguous));
    }

    [Fact]
    public async Task Action_without_verb_is_rejected_and_manual_terminal_status_clears_holding()
    {
        var store = Store(); var workspace = await store.CreateAsync("Invalid action");
        workspace = await store.AddEntityAsync(workspace.Id, "ActorA", "Character");
        workspace = await store.AddEntityAsync(workspace.Id, "Bread", "Object");
        var actor = workspace.Entities[0]; var bread = workspace.Entities[1];
        workspace = await store.SetHolderAsync(workspace.Id, bread.Id, actor.Id, null);
        var preview = new ScenePromptPreview(Guid.NewGuid().ToString("N"), ScenePromptReducer.Revision(workspace), "ActorA eats Bread", "", "test",
            new(Array.Empty<ScenePromptEntity>(), new[] { new ScenePromptChange(actor.Id, "action", "", bread.Id, false, "ActorA eats Bread") }, Array.Empty<string>()));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => store.ApplyPromptAsync(workspace.Id, preview));
        Assert.Single((await store.GetAsync(workspace.Id)).History);
        await store.RecordAsync(workspace.Id, bread.Id, "status", "consumed", "Manual correction");
        Assert.DoesNotContain((await store.QueryAsync(workspace.Id, new(actor.Id))).Facts, f => f.Property == "holding");
    }

    [Theory]
    [InlineData("has")]
    [InlineData("holds")]
    [InlineData("holding")]
    [InlineData("carries")]
    [InlineData("carrying")]
    public async Task Actor_side_possession_is_stored_on_object_with_pocket_detail_and_transfer_clears_old_position(string verb)
    {
        var store = Store(); var workspace = await store.CreateAsync("Pocket regression");
        workspace = await store.AddEntityAsync(workspace.Id, "ActorA", "Character");
        workspace = await store.AddEntityAsync(workspace.Id, "ActorB", "Character");
        var a = workspace.Entities[0]; var b = workspace.Entities[1];
        const string prompt = "ActorA has an iphone on his pocket and carries keys.";
        var preview = new ScenePromptPreview(Guid.NewGuid().ToString("N"), ScenePromptReducer.Revision(workspace), prompt, "", "test",
            new(new[] { new ScenePromptEntity("phone", "iPhone", "Object"), new ScenePromptEntity("keys", "Keys", "Object") },
                new[] { new ScenePromptChange(a.Id, verb, "", "phone", false, "ActorA has an iphone on his pocket"),
                    new ScenePromptChange("phone", "carried_at", "ActorA's pocket", "", false, "on his pocket"),
                    new ScenePromptChange(a.Id, "has", "keys", "", false, "carries keys") }, Array.Empty<string>()));
        workspace = await store.ApplyPromptAsync(workspace.Id, preview);
        var phone = workspace.Entities.Single(e => e.Name == "iPhone");
        var facts = (await Store().QueryAsync(workspace.Id, new(phone.Id))).Facts;
        Assert.Contains(facts, f => f.Property == "held_by" && f.RelatedEntityId == a.Id);
        Assert.Contains(facts, f => f.Property == "carried_at" && f.Value == "ActorA's pocket");
        Assert.Equal(2, (await store.QueryAsync(workspace.Id, new(a.Id))).Facts.Count(f => f.Property == "holding"));
        Assert.DoesNotContain(workspace.History, f => f.EntityId == a.Id && f.Property == verb);
        var atPocket = workspace.History.Count;
        workspace = await store.RecordAsync(workspace.Id, a.Id, "has", "iPhone", "Legacy actor-side entry");
        var transfer = new ScenePromptPreview(Guid.NewGuid().ToString("N"), ScenePromptReducer.Revision(workspace), "ActorA gives the phone to ActorB.", "", "test",
            new(Array.Empty<ScenePromptEntity>(), new[] { new ScenePromptChange(b.Id, "has", "", phone.Id, false, "ActorA gives the phone to ActorB") }, Array.Empty<string>()));
        workspace = await store.ApplyPromptAsync(workspace.Id, transfer);
        facts = (await store.QueryAsync(workspace.Id, new(phone.Id))).Facts;
        Assert.Contains(facts, f => f.Property == "held_by" && f.RelatedEntityId == b.Id);
        Assert.DoesNotContain(facts, f => f.Property == "carried_at");
        Assert.DoesNotContain((await store.QueryAsync(workspace.Id, new(a.Id))).Facts, f => f.Value == "iPhone");
        Assert.Contains((await store.QueryAsync(workspace.Id, new(phone.Id, AtSequence: atPocket))).Facts,
            f => f.Property == "carried_at" && f.Value == "ActorA's pocket");
        var wrongRelease = transfer with { Id = Guid.NewGuid().ToString("N"), Revision = ScenePromptReducer.Revision(workspace),
            Extraction = new(Array.Empty<ScenePromptEntity>(), new[] { new ScenePromptChange(a.Id, verb, "", phone.Id, true, "ActorA") }, Array.Empty<string>()) };
        await Assert.ThrowsAnyAsync<ArgumentException>(() => store.ApplyPromptAsync(workspace.Id, wrongRelease));
    }

    [Fact]
    public async Task Ownership_does_not_imply_carrying_and_manual_transfer_clears_pocket()
    {
        var store = Store(); var workspace = await store.CreateAsync("Ownership");
        workspace = await store.AddEntityAsync(workspace.Id, "ActorA", "Character");
        workspace = await store.AddEntityAsync(workspace.Id, "ActorB", "Character");
        workspace = await store.AddEntityAsync(workspace.Id, "Phone", "Object");
        var a = workspace.Entities[0]; var b = workspace.Entities[1]; var phone = workspace.Entities[2];
        var preview = new ScenePromptPreview(Guid.NewGuid().ToString("N"), ScenePromptReducer.Revision(workspace), "ActorA owns Phone.", "", "test",
            new(Array.Empty<ScenePromptEntity>(), new[] { new ScenePromptChange(phone.Id, "owned_by", "", a.Id, false, "ActorA owns Phone") }, Array.Empty<string>()));
        await store.ApplyPromptAsync(workspace.Id, preview);
        Assert.DoesNotContain((await store.QueryAsync(workspace.Id, new(phone.Id))).Facts, f => f.Property == "held_by");
        await store.SetHolderAsync(workspace.Id, phone.Id, a.Id, null);
        await store.RecordAsync(workspace.Id, phone.Id, "carried_at", "ActorA's pocket", null);
        await store.SetHolderAsync(workspace.Id, phone.Id, b.Id, null);
        Assert.DoesNotContain((await store.QueryAsync(workspace.Id, new(phone.Id))).Facts, f => f.Property == "carried_at");
    }

    [Theory]
    [InlineData("owned_by", "a", "red hat", "", false)]
    [InlineData("owned_by", "a", "", "hat", true)]
    [InlineData("owned_by", "hat", "ActorA", "", false)]
    [InlineData("owned_by", "hat", "unknown owner", "", true)]
    [InlineData("owns", "a", "red hat", "", false)]
    public void Ownership_requires_an_identified_item_and_owner(string property, string subject, string value, string target, bool declareHat)
    {
        var workspace = new SceneWorkspace("s", "Scene", DateTimeOffset.UtcNow,
            new[] { new SceneEntity("a", "ActorA", "Character") }, Array.Empty<SceneFact>());
        var plan = new ScenePromptPreview("p", ScenePromptReducer.Revision(workspace), "ActorA owns a red hat", "", "test",
            new(declareHat ? new[] { new ScenePromptEntity("hat", "Red hat", "Object") } : Array.Empty<ScenePromptEntity>(),
                new[] { new ScenePromptChange(subject, property, value, target, false, "ActorA owns a red hat") }, Array.Empty<string>()));
        Assert.ThrowsAny<ArgumentException>(() => ScenePromptReducer.Apply(workspace, plan));
    }

    [Fact]
    public async Task Ownership_discovers_objects_supports_reverse_queries_and_survives_borrowing()
    {
        var store = Store(); var workspace = await store.CreateAsync("Ownership discovery");
        workspace = await store.AddEntityAsync(workspace.Id, "ActorA", "Character");
        workspace = await store.AddEntityAsync(workspace.Id, "ActorB", "Character");
        var a = workspace.Entities[0]; var b = workspace.Entities[1];
        const string prompt = "ActorA owns a red hat and a blue bag.";
        var plan = new ScenePromptPreview("ownership", ScenePromptReducer.Revision(workspace), prompt, "", "test", new(
            new[] { new ScenePromptEntity("hat", "Red hat", "Object"), new ScenePromptEntity("bag", "Blue bag", "Object") },
            new[] { new ScenePromptChange("hat", "owned_by", a.Id, "", false, prompt),
                new ScenePromptChange("hat", "color", "red", "", false, "red hat"),
                new ScenePromptChange(a.Id, "owns", "", "bag", false, prompt) }, Array.Empty<string>()));
        workspace = await store.ApplyPromptAsync(workspace.Id, plan);
        var hat = workspace.Entities.Single(e => e.Name == "Red hat");
        Assert.Equal(2, (await store.QueryAsync(workspace.Id, new(a.Id))).Facts.Count(f => f.Property == "owns"));
        Assert.DoesNotContain(workspace.History, f => f.Property == "held_by");
        Assert.Contains((await store.QueryAsync(workspace.Id, new(hat.Id))).Facts, f => f.Property == "owned_by" && f.RelatedEntityId == a.Id);
        var beforeBorrow = workspace.History.Count;
        workspace = await store.SetHolderAsync(workspace.Id, hat.Id, b.Id, "ActorB borrows the hat");
        var facts = (await Store().QueryAsync(workspace.Id, new(hat.Id))).Facts;
        Assert.Contains(facts, f => f.Property == "owned_by" && f.RelatedEntityId == a.Id);
        Assert.Contains(facts, f => f.Property == "held_by" && f.RelatedEntityId == b.Id);
        var transfer = new ScenePromptPreview("owner-change", ScenePromptReducer.Revision(workspace), "ActorB now owns the red hat.", "", "test",
            new(Array.Empty<ScenePromptEntity>(), new[] { new ScenePromptChange(hat.Id, "owned_by", "", b.Id, false, "ActorB now owns the red hat") }, Array.Empty<string>()));
        workspace = await store.ApplyPromptAsync(workspace.Id, transfer);
        Assert.Single(workspace.Entities.Where(e => e.Name == "Red hat"));
        Assert.DoesNotContain((await store.QueryAsync(workspace.Id, new(a.Id))).Facts, f => f.Property == "owns" && f.RelatedEntityId == hat.Id);
        Assert.Contains((await store.QueryAsync(workspace.Id, new(a.Id, AtSequence: beforeBorrow))).Facts, f => f.Property == "owns" && f.RelatedEntityId == hat.Id);
        var wrongRelease = transfer with { Id = "wrong-release", Revision = ScenePromptReducer.Revision(workspace), Extraction = new(Array.Empty<ScenePromptEntity>(),
            new[] { new ScenePromptChange(a.Id, "owns", "", hat.Id, true, "red hat") }, Array.Empty<string>()) };
        await Assert.ThrowsAnyAsync<ArgumentException>(() => store.ApplyPromptAsync(workspace.Id, wrongRelease));
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
