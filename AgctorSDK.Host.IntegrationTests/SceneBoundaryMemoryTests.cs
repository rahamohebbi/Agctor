using AgctorSDK.Core.SceneMemory;
using AgctorSDK.Host.Services.VideoWorkflow;
using Xunit;
namespace AgctorSDK.Host.IntegrationTests;

public sealed class SceneBoundaryMemoryTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), "boundaries-" + Guid.NewGuid().ToString("N") + ".db");
    [Fact]
    public async Task Observations_are_lookupable_by_shot_and_take_without_rewriting_planned_state()
    {
        var store = new SqliteSceneMemoryStore(path);
        var workspace = await store.CreateAsync("Garden");
        workspace = await store.ApplyPromptAsync(workspace.Id, new("shot", ScenePromptReducer.Revision(workspace), "Enter", "Entrance", "plan-model", new([], [], []), DurationSeconds: 15));
        var revision = ScenePromptReducer.Revision(workspace);
        var item = new SceneBoundaryObservation("shot", "run-1-1", DateTimeOffset.UtcNow, "vision-model", [13.5, 14.25, 14.95],
            new("observed", "Inside greenhouse", "Moving toward bench", "Pot in arms", "Entered greenhouse", "Rear view"), MediaDurationSeconds: 15);
        await store.SaveBoundaryAsync(workspace.Id, item); await store.SaveBoundaryAsync(workspace.Id, item);
        await store.SaveBoundaryAsync(workspace.Id, item with { RenderKey = "run-1-2" });
        store = new SqliteSceneMemoryStore(path);
        Assert.Equal(revision, ScenePromptReducer.Revision(await store.GetAsync(workspace.Id)));
        Assert.Equal(2, (await store.ReadBoundariesAsync(workspace.Id)).Count);
        Assert.Equal(item, Assert.Single(await store.ReadBoundariesAsync(workspace.Id, "shot", "run-1-1")) with { SampleSeconds = item.SampleSeconds });
        Assert.Empty(await store.ReadBoundariesAsync(workspace.Id, "another-shot"));
        var other = await store.CreateAsync("Other"); Assert.Empty(await store.ReadBoundariesAsync(other.Id));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.SaveBoundaryAsync(other.Id, item));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveBoundaryAsync(workspace.Id, item with { SampleSeconds = [14, 13, 12] }));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveBoundaryAsync(workspace.Id, item with { SampleSeconds = [14, 15, 16] }));
        await store.SaveBoundaryAsync(workspace.Id, item with { RenderKey = "run-1-3", MediaDurationSeconds = 15.083, SampleSeconds = [13.583, 14.333, 15.033] });
        Assert.Equal(3, (await store.ReadBoundariesAsync(workspace.Id)).Count);
        await store.DeleteShotAsync(workspace.Id, "shot", revision);
        Assert.Empty(await store.ReadBoundariesAsync(workspace.Id));
    }
    [Fact]
    public void Retired_take_observations_never_guide_a_new_take()
    {
        var previous = new SceneVideoClip { ShotId = "run-1", RenderAttempt = 2, Boundary = new("run-1", "run-1-1", DateTimeOffset.UtcNow,
            "vision", [13, 14, 14.9], new("observed", "SECRET OLD POSITION", "Walking", "Pot", "Entered", "Rear")) };
        var next = new SceneVideoClip { ShotId = "run-2", VideoPrompt = "Place pot", Prompt = "Place pot" };
        var state = new SceneVideoState { Current = 1, Clips = [previous, next], Input = new("r", "w", "Story") };
        var instruction = SceneBoundaryGuidance.Instruction(state, next);
        Assert.DoesNotContain("SECRET OLD POSITION", instruction);
        Assert.Contains("unverified", instruction);
    }

    [Fact]
    public void Continue_run_clip_one_uses_prior_film_observed_boundary_guidance()
    {
        var priorBoundary = new SceneBoundaryObservation(
            "prev-shot-2", "prev-run-2-1", DateTimeOffset.UtcNow, "vision-model", [13.5, 14.25, 14.95],
            new("observed", "Fox beside glowing moonflower", "Looking up toward balcony", "Blue scarf around neck", "Garden bloomed", "Low angle medium shot"),
            MediaDurationSeconds: 15);
        var clip3 = new SceneVideoClip { ShotId = "run-3-1", VideoPrompt = "Camera rises to the moon", Prompt = "Camera rises to the moon" };
        var state = new SceneVideoState
        {
            Current = 0,
            Clips = [clip3],
            Input = new("r3", "w", "Zoom to the moon", StoryMode: "continue"),
            PriorFilmBoundary = priorBoundary,
        };
        var instruction = SceneBoundaryGuidance.Instruction(state, clip3);
        Assert.NotNull(instruction);
        Assert.Contains("CONTINUE THE OBSERVED MOVEMENT", instruction);
        Assert.Contains("Fox beside glowing moonflower", instruction);
        Assert.Contains("Lock frame 1", instruction);
    }
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
}
