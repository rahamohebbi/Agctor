using System.Net;
using System.Net.Http.Json;
using AgctorSDK.Core.SceneMemory;
using AgctorSDK.Core.Tools.Implementations;
using AgctorSDK.Core.Tools.Models;
using AgctorSDK.Host.Models.SceneMemory;
using AgctorSDK.Host.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class SceneTimedEventsTests
{
    private static SceneWorkspace Story() {
        var w = new SceneWorkspace("w", "Sword", DateTimeOffset.UtcNow, [new("ryan", "Ryan", "Character"), new("sword", "Sword", "Object")], []);
        return ScenePromptReducer.Apply(w, new("shot", ScenePromptReducer.Revision(w), "Ryan lifts the sword.", "Lift", "test", new([], [], [], []), DurationSeconds: 10));
    }
    private static SceneTimedEvent Event(string id = "event", long start = 4000, long end = 7000) => new(id, "action", "Ryan lifts the sword", new(start, end), ["ryan", "sword"]);
    private static SceneWorkspace Save(SceneWorkspace w, SceneTimedEvent e) => SceneTimedEvents.Save(w, "shot", ScenePromptReducer.Revision(w), e);

    [Fact]
    public void Search_combines_names_text_kind_and_half_open_overlap_with_stable_pagination() {
        var w = Save(Save(Story(), Event("one", 2000, 5000)), Event("two", 5000, 7000));
        Assert.Equal(2, SceneTimedEvents.Query(w, new(Text: "RYAN sword", EntityId: "sword", Kind: "action", Overlap: new(4000, 7000))).Total);
        Assert.Equal("two", Assert.Single(SceneTimedEvents.Query(w, new(Overlap: new(5000, 7000))).Matches).Event.Id);
        Assert.Empty(SceneTimedEvents.Query(w, new(Overlap: new(7000, 8000))).Matches);
        Assert.Equal("two", Assert.Single(SceneTimedEvents.Query(w, new(Offset: 1, Limit: 1)).Matches).Event.Id);
        Assert.Empty(SceneTimedEvents.Query(w, new(EntityId: "missing")).Matches);
        Assert.Throws<ArgumentException>(() => SceneTimedEvents.Query(w, new(Overlap: new(7000, 4000))));
        Assert.Throws<ArgumentException>(() => SceneTimedEvents.Query(w, new(Limit: 201)));
        Assert.Throws<InvalidOperationException>(() => SceneTimedEvents.Query(w, new(ExpectedRevision: "stale")));
    }

    [Fact]
    public void Rational_clocks_are_preserved_and_take_observations_never_replace_planned_events() {
        var planned = Event() with { Timing = new(120, 210, 30000, 1001), Protected = true, Before = "Sword down", After = "Sword raised" };
        var w = Save(Story(), planned);
        Assert.Throws<ArgumentException>(() => Save(w, planned with { Status = "confirmed", TakeId = "take-1" }));
        w = Save(w, Event("observed") with { Status = "confirmed", TakeId = "take-1", Description = "Take-only observation", Timing = new(192000, 336000, 48000) });
        Assert.Equal(2, SceneTimedEvents.Query(w, new()).Total);
        Assert.Single(SceneTimedEvents.Query(w, new(TakeId: "take-1")).Matches);
        Assert.Equal(30000, w.Prompts![0].TimedEvents![0].Id == "event" ? w.Prompts[0].TimedEvents![0].Timing.RateNumerator : w.Prompts[0].TimedEvents![1].Timing.RateNumerator);
        var prompt = SceneVideoPromptBuilder.Prepare(w, "shot", ScenePromptReducer.Revision(w)).Prompt;
        Assert.Contains("4.004–7.007s", prompt);
        Assert.Contains("Sword down", prompt);
        Assert.DoesNotContain("Take-only observation", prompt);
        Assert.Throws<ArgumentException>(() => Save(w, Event("bad") with { Status = "confirmed" }));
        Assert.Throws<ArgumentException>(() => Save(w, Event("bad") with { TakeId = "take-1" }));
        Assert.Throws<ArgumentException>(() => Save(w, Event("bad", 0, 11000)));
        Assert.Throws<ArgumentException>(() => Save(w, Event("bad") with { EntityIds = ["future"] }));
    }

    [Fact]
    public void Revisions_and_relationships_are_checked_and_deletion_removes_links() {
        var original = Story(); var w = Save(original, Event());
        Assert.Throws<InvalidOperationException>(() => SceneTimedEvents.Save(w, "shot", ScenePromptReducer.Revision(original), Event()));
        w = Save(w, Event("sound") with { Kind = "sound", RelatedEventIds = ["event"] });
        Assert.Throws<ArgumentException>(() => Save(w, Event() with { Status = "confirmed", TakeId = "take" }));
        w = SceneTimedEvents.Delete(w, "shot", "event", ScenePromptReducer.Revision(w));
        Assert.Empty(Assert.Single(w.Prompts![0].TimedEvents!).RelatedEventIds!);
        Assert.Equal(original.History, w.History);
        Assert.Throws<KeyNotFoundException>(() => SceneTimedEvents.Delete(w, "shot", "missing", ScenePromptReducer.Revision(w)));
    }

    [Fact]
    public async Task Timings_persist_across_store_restart_and_are_available_to_agent_lookup() {
        var directory = Path.Combine(Path.GetTempPath(), "scene-timing-" + Guid.NewGuid().ToString("N"));
        try {
            var db = Path.Combine(directory, "scene.db");
            var store = new SqliteSceneMemoryStore(db);
            var w = await store.CreateAsync("Story");
            w = await store.AddEntityAsync(w.Id, "Ryan", "Character");
            var id = w.Entities.Single().Id;
            w = await store.ApplyPromptAsync(w.Id, new("shot", ScenePromptReducer.Revision(w), "Ryan smiles", "Smile", "test", new([], [], [], []), DurationSeconds: 10));
            w = await store.SaveTimedEventAsync(w.Id, "shot", ScenePromptReducer.Revision(w), Event() with { EntityIds = [id] });
            var revision = ScenePromptReducer.Revision(w);
            var reopened = new SqliteSceneMemoryStore(db);
            Assert.Equal(revision, ScenePromptReducer.Revision(await reopened.GetAsync(w.Id)));
            Assert.Single((await reopened.QueryAsync(w.Id, new(id))).TimedEvents!);
            var tool = new SceneMemoryQueryTool("timed-test", reopened);
            var result = await tool.Handle(new ToolRequest { Operation = "SearchEvents", Parameters = new Dictionary<string, object> { ["workspaceId"] = w.Id, ["text"] = "Ryan", ["startMs"] = 4500, ["endMs"] = 6000 } });
            Assert.True(result.IsSuccess, result.Error);
            Assert.Single(Assert.IsType<SceneTimedEventResult>(result.Output).Matches);
            await Assert.ThrowsAsync<ArgumentException>(() => reopened.UpdatePromptMetadataAsync(w.Id, "shot", "", "Smile", 3));
            await reopened.DeleteShotAsync(w.Id, "shot", revision);
            Assert.Empty(SceneTimedEvents.Query(await reopened.GetAsync(w.Id), new()).Matches);
        } finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("00:04–00:07 Ryan speaks", true)]
    [InlineData("From 4 to 7 seconds Ryan speaks", true)]
    [InlineData("from 4 seconds to 7 seconds", true)]
    [InlineData("00:00:04–00:00:07", true)]
    [InlineData("A 7-second clip. Ryan speaks", false)]
    [InlineData("Ryan speaks for 3 seconds", false)]
    [InlineData("Ryan carries 4-7 swords", false)]
    [InlineData("00:05–00:07 Ryan speaks", false)]
    public void Interpreted_timing_requires_matching_explicit_evidence(string evidence, bool expected) =>
        Assert.Equal(expected, SceneTimingEvidence.Supports(evidence, new(4000, 7000)));

    [Fact]
    public void Explicit_prompt_timing_is_reviewable_and_applied_as_planned() {
        var w = new SceneWorkspace("w", "Story", DateTimeOffset.UtcNow, [new("ryan", "Ryan", "Character")], []);
        const string prompt = "00:04–00:07 Ryan says hello.";
        var preview = new ScenePromptPreview("shot", ScenePromptReducer.Revision(w), prompt, "Hello", "test",
            new([], [], [], [new("Ryan speaks", ["ryan"], prompt, Timing: new(4000, 7000), Kind: "dialogue", Dialogue: "hello")]), DurationSeconds: 10);
        var applied = ScenePromptReducer.Apply(w, preview);
        Assert.Empty(w.Prompts ?? []);
        var saved = Assert.Single(applied.Prompts![0].TimedEvents!);
        Assert.Equal("planned", saved.Status); Assert.Null(saved.TakeId); Assert.Equal("hello", saved.Dialogue);
    }

    [Fact]
    public void Prompt_without_an_explicit_range_does_not_grow_a_planned_event() {
        var w = new SceneWorkspace("w", "Story", DateTimeOffset.UtcNow, [new("ryan", "Ryan", "Character")], []);
        const string prompt = "Ryan says hello.";
        var preview = new ScenePromptPreview("shot", ScenePromptReducer.Revision(w), prompt, "Hello", "test",
            new([], [], [], [new("Ryan speaks", ["ryan"], prompt)]), DurationSeconds: 10);
        var applied = ScenePromptReducer.Apply(w, preview);
        Assert.Empty(applied.Prompts![0].TimedEvents ?? []);
    }

    [Fact]
    public void Confirmed_dialogue_keeps_the_planned_event_and_story_facts() {
        var planned = Event("plan") with { Kind = "dialogue", Dialogue = "hello" };
        var w = Save(Story(), planned);
        var history = w.History;
        var confirmed = new SceneTimedEvent("spoken", "dialogue", "Measured spoken line", new(4000, 9000), [], "confirmed", "render-key", "Hello my friend");
        w = Save(w, confirmed);
        Assert.Equal(history, w.History);
        Assert.Equal(w.Entities, Story().Entities);
        Assert.Equal("planned", w.Prompts![0].TimedEvents!.Single(e => e.Id == "plan").Status);
        var spoken = w.Prompts[0].TimedEvents!.Single(e => e.Id == "spoken");
        Assert.Equal("confirmed", spoken.Status);
        Assert.Equal("render-key", spoken.TakeId);
        Assert.Equal("Hello my friend", spoken.Dialogue);
    }
}

[Collection("LlmStatic")]
public sealed class SceneTimedEventsHttpTests(SceneStudioWebApplicationFactory factory) : IClassFixture<SceneStudioWebApplicationFactory>
{
    [Fact]
    public async Task Public_contract_saves_searches_rejects_stale_edits_and_deletes() {
        using var client = factory.CreateClient();
        async Task<T> Read<T>(HttpResponseMessage r) { Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return (await r.Content.ReadFromJsonAsync<T>())!; }
        var w = await Read<SceneMemoryWorkspaceResponse>(await client.PostAsJsonAsync("/api/scene-memory/v1/workspaces/connect", new ConnectStoryRequest("Timing", "cynelo", Guid.NewGuid().ToString("N"), "story")));
        var store = factory.Services.GetRequiredService<ISceneMemoryStore>();
        var source = await store.GetAsync(w.Id);
        source = await store.ApplyPromptAsync(w.Id, new("shot", ScenePromptReducer.Revision(source), "Quiet scene", "Quiet", "test", new([], [], [], []), DurationSeconds: 10));
        var path = $"/api/scene-memory/v1/workspaces/{w.Id}";
        var revision = ScenePromptReducer.Revision(source);
        var item = new SceneTimedEvent("sound", "sound", "Bell rings", new(4000, 7000), []);
        var timeline = await Read<SceneTimelineResponse>(await client.PutAsJsonAsync(path + "/shots/shot/events/sound", new SaveSceneTimedEventRequest(revision, item)));
        Assert.Single(timeline.Shots[0].TimedEvents!);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(path + "/shots/shot/events/sound", new SaveSceneTimedEventRequest(revision, item))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(path + "/shots/shot/events/different", new SaveSceneTimedEventRequest(timeline.Revision, item))).StatusCode);
        var results = await Read<SceneTimedEventResult>(await client.PostAsJsonAsync(path + "/events/search", new SceneTimedEventQuery(Text: "bell", Overlap: new(6000, 8000))));
        Assert.Single(results.Matches);
        var deleted = await Read<SceneTimelineResponse>(await client.SendAsync(new(HttpMethod.Delete, path + "/shots/shot/events/sound") { Content = JsonContent.Create(new PrepareSceneVideoRequest(timeline.Revision)) }));
        Assert.Empty(deleted.Shots[0].TimedEvents!);
    }
}
