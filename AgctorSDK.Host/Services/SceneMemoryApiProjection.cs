using AgctorSDK.Core.SceneMemory;
using AgctorSDK.Host.Models.SceneMemory;

namespace AgctorSDK.Host.Services;

/// <summary>UI-independent projections used by HTTP clients, including Cynelo.</summary>
public static class SceneMemoryApiProjection
{
    public static SceneMemoryWorkspaceResponse Workspace(SceneWorkspace w) =>
        new(w.Id, w.Name, ScenePromptReducer.Revision(w), w.Connection, w.Entities, w.History.Count);

    public static SceneMemoryPreviewResponse Preview(ScenePromptReview review) => new(review.Preview,
        Workspace(review.Proposed), review.Proposed.History.Where(f => f.PromptId == review.Preview.Id).ToArray());

    public static SceneTimelineResponse Timeline(SceneWorkspace w)
    {
        Dictionary<(string, string), SceneFact> State(long sequence) => w.History.Where(f => !f.IsEvent && f.Sequence <= sequence)
            .GroupBy(f => (f.EntityId, f.Property.ToLowerInvariant())).ToDictionary(g => g.Key, g => g.Last());
        var shots = (w.Prompts ?? []).Select((p, index) =>
        {
            var before = State(p.FirstSequence - 1);
            var after = State(p.LastSequence);
            var changes = w.History.Where(f => !f.IsEvent && f.PromptId == p.Id)
                .Select(f => (f.EntityId, Property: f.Property.ToLowerInvariant())).Distinct()
                .Select(key => new SceneTimelineDelta(key.EntityId, key.Property,
                    before.TryGetValue(key, out var old) && !old.Removed ? old : null,
                    after.TryGetValue(key, out var next) && !next.Removed ? next : null)).ToArray();
            return new SceneTimelineShot(p.Id, index + 1, p.SceneLabel, p.Label, p.Prompt, p.DurationSeconds,
                p.FirstSequence, p.LastSequence, p.IntroducedEntityIds ?? [], changes,
                w.History.Where(f => f.IsEvent && f.PromptId == p.Id).ToArray(), after.Values.Where(f => !f.Removed).ToArray());
        }).ToArray();
        return new(w.Id, ScenePromptReducer.Revision(w), w.Entities, shots);
    }
}
