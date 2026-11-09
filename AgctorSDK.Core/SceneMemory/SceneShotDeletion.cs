using System;
using System.Collections.Generic;
using System.Linq;

namespace AgctorSDK.Core.SceneMemory;

/// <summary>Remove an approved shot without reinterpreting or rewriting any other shot's actions.</summary>
public static class SceneShotDeletion
{
    public static SceneWorkspace Delete(SceneWorkspace workspace, string shotId, string expectedRevision)
    {
        if (string.IsNullOrWhiteSpace(expectedRevision) || expectedRevision.Length > 128)
            throw new ArgumentException("Refresh the story before deleting a shot.");
        if (ScenePromptReducer.Revision(workspace) != expectedRevision)
            throw new InvalidOperationException("The story changed. Refresh it and review the shot before deleting it.");
        if (workspace.Prompts?.Any(p => p.Id == shotId) != true)
            throw new KeyNotFoundException("Shot not found in this story.");

        var retained = workspace.History.Where(f => f.PromptId != shotId).ToArray();
        var sequences = retained.Select(f => f.Sequence).ToArray();
        // History uses dense, one-based sequence numbers throughout the store. Translate
        // boundaries too, including empty shots, manual facts and entity creation times.
        long Boundary(long old)
        {
            var index = Array.BinarySearch(sequences, old);
            return index >= 0 ? index + 1L : ~index;
        }
        return workspace with {
            History = retained.Select((f, i) => f with { Sequence = i + 1L }).ToArray(),
            Prompts = workspace.Prompts!.Where(p => p.Id != shotId).Select(p => p with {
                FirstSequence = Boundary(p.FirstSequence - 1) + 1,
                LastSequence = Boundary(p.LastSequence),
                // Keep legacy membership stable when removed sequence ranges collapse.
                // This freezes identity membership, not designs or their current versions.
                KnownEntityIds = p.KnownEntityIds ?? (p.DesignSnapshots == null
                    ? workspace.Entities.Where(e => e.CreatedAtSequence == null || e.CreatedAtSequence <= p.LastSequence).Select(e => e.Id).ToArray()
                    : null)
            }).ToArray(),
            Entities = workspace.Entities.Select(e => e with {
                CreatedAtSequence = e.CreatedAtSequence is { } sequence ? Boundary(sequence) : null
            }).ToArray(),
            // Prevent delayed apply retries from resurrecting a deleted shot and ensure
            // deletion never restores an old revision, even for a shot with no facts.
            DeletedShotIds = (workspace.DeletedShotIds ?? Array.Empty<string>()).Append(shotId).ToArray()
        };
    }
}
