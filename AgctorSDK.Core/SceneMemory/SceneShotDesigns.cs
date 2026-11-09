using System;
using System.Collections.Generic;
using System.Linq;

namespace AgctorSDK.Core.SceneMemory;

public sealed record SceneDesignDifference(string EntityId, string Name, string Kind,
    SceneStudioBinding? Saved, SceneStudioBinding? Current);
public sealed record SceneShotDesignReview(string ShotId, string Revision, bool IsPinned,
    IReadOnlyList<SceneEntity> Saved, IReadOnlyList<SceneEntity> Current, IReadOnlyList<SceneDesignDifference> Updates);

/// <summary>Shot appearance is a self-contained copy, independent of mutable library records and scene facts.</summary>
public static class SceneShotDesigns
{
    public static SceneEntity[] Capture(IEnumerable<SceneEntity> entities) => entities.OrderBy(e => e.Id, StringComparer.Ordinal)
        .Select(e => e with { Studio = e.Studio is { } s ? s with {
            Defaults = s.Defaults.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal)
        } : null }).ToArray();

    public static SceneShotDesignReview Review(SceneWorkspace workspace, string shotId)
    {
        var shot = workspace.Prompts?.SingleOrDefault(p => p.Id == shotId)
            ?? throw new KeyNotFoundException("Shot not found in this story.");
        // Membership is frozen too: designs for entities introduced in later shots must not leak backwards.
        var ids = (shot.DesignSnapshots?.Select(e => e.Id) ?? shot.KnownEntityIds)?.ToHashSet(StringComparer.Ordinal);
        var current = Capture(workspace.Entities.Where(e => ids != null ? ids.Contains(e.Id)
            : e.CreatedAtSequence == null || e.CreatedAtSequence <= shot.LastSequence));
        var saved = shot.DesignSnapshots ?? Array.Empty<SceneEntity>();
        var old = saved.ToDictionary(e => e.Id);
        var updates = current.Where(e => !Same(old.GetValueOrDefault(e.Id)?.Studio, e.Studio))
            .Select(e => new SceneDesignDifference(e.Id, e.Name, e.Kind, old.GetValueOrDefault(e.Id)?.Studio, e.Studio)).ToArray();
        return new(shotId, ScenePromptReducer.Revision(workspace), shot.DesignSnapshots != null, saved, current, updates);
    }

    private static bool Same(SceneStudioBinding? a, SceneStudioBinding? b) => a == null ? b == null : b != null &&
        a.InstanceKey == b.InstanceKey && a.AssetId == b.AssetId && a.Version == b.Version && a.DisplayName == b.DisplayName &&
        a.Defaults.Count == b.Defaults.Count && a.Defaults.All(p => b.Defaults.TryGetValue(p.Key, out var value) && value == p.Value);

    public static SceneWorkspace Pin(SceneWorkspace workspace, string shotId, string expectedRevision)
    {
        var review = Review(workspace, shotId);
        if (review.Revision != expectedRevision)
            throw new InvalidOperationException("The story or designs changed. Close this review and review the latest designs before updating this shot.");
        if (review.IsPinned && review.Updates.Count == 0) return workspace;
        return workspace with { Prompts = workspace.Prompts!.Select(p => p.Id == shotId
            ? p with { DesignSnapshots = Capture(review.Current) } : p).ToArray() };
    }
}
