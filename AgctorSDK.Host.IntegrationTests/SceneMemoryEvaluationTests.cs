using AgctorSDK.Core.SceneMemory;
using AgctorSDK.Host.Services;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class SceneMemoryEvaluationTests
{
    [Fact]
    public void Catalog_has_100_unique_cases_across_the_scene_domains()
    {
        var catalog = SceneMemoryEvaluationService.Catalog;
        Assert.Equal(100, catalog.Count);
        Assert.Equal(100, catalog.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(new[] { "intent", "lifecycle", "locations", "objects", "ownership", "people", "possession", "transfer" },
            catalog.Select(c => c.Category).Distinct().Order().ToArray());
        Assert.All(catalog, c =>
        {
            Assert.NotEmpty(c.Prompts);
            Assert.NotEmpty(c.ExpectedEntities);
        });
    }

    [Fact]
    public void Scoring_checks_final_semantic_state_and_forbidden_facts()
    {
        var testCase = new SceneEvaluationCase("ownership", "ownership", "Owns a hat", ["Mara owns a red hat."],
            [new("m", "Mara", "Character"), new("h", "Red hat", "Object")],
            [new("Red hat", "owned_by", Target: "Mara"), new("Red hat", "color", "red")],
            [new("Red hat", "held_by")]);
        var workspace = new SceneWorkspace("eval", "Eval", DateTimeOffset.UtcNow,
            [new("m", "Mara", "Character"), new("h", "Red hat", "Object")],
            [new(1, "h", "owned_by", "Mara", "owns", DateTimeOffset.UtcNow, "m"),
             new(2, "h", "color", "red", "red hat", DateTimeOffset.UtcNow)]);

        var checks = SceneMemoryEvaluationService.Score(testCase, workspace, 0);

        Assert.All(checks, c => Assert.True(c.Passed, c.Expected));
    }

    [Fact]
    public async Task Runner_uses_an_isolated_scene_and_completes_a_selected_case()
    {
        using var service = new SceneMemoryEvaluationService(new PossessionInterpreter());
        var started = service.Start(new(["test-model"], ["possession-01"]));
        SceneEvaluationJobSnapshot snapshot = started;
        for (var attempt = 0; attempt < 50 && snapshot.Status is "queued" or "running"; attempt++)
        {
            await Task.Delay(10);
            snapshot = service.Get(started.Id);
        }

        Assert.Equal("completed", snapshot.Status);
        var result = Assert.Single(snapshot.Results);
        Assert.True(result.Passed);
        Assert.Equal("Mara", result.ActualState!.Entities.Single(e => e.Kind == "Character").Name);
    }

    [Fact]
    public void Quick_run_is_balanced_across_categories()
    {
        var selected = SceneMemoryEvaluationService.TakeBalanced(SceneMemoryEvaluationService.Catalog, 10).ToArray();

        Assert.Equal(10, selected.Length);
        Assert.Equal(8, selected.Select(c => c.Category).Distinct().Count());
    }

    private sealed class PossessionInterpreter : IScenePromptInterpreter
    {
        public Task<ScenePromptExtraction> InterpretAsync(SceneWorkspace workspace, string prompt, string model, CancellationToken ct) =>
            Task.FromResult(new ScenePromptExtraction(
                [new("m", "Mara", "Character"), new("k", "Key", "Object")],
                [new("k", "held_by", "", "m", false, "Mara carries the key")],
                [], [new("Mara carries the key", ["m", "k"], "Mara carries the key", "ongoing")]));
    }
}
