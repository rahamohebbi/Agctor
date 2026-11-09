using AgctorSDK.Core.SceneMemory;

namespace AgctorSDK.Host.Services.VideoWorkflow;

/// <summary>Read-only retrieval scope: library availability never grants participation in a new video task.</summary>
public static class SceneVideoMemoryScope
{
    public static SceneWorkspace Select(SceneWorkspace workspace, SceneVideoState state, int? completedClips = null)
    {
        var intent = state.Input.Prompt + "\n" + state.Clarifications;
        var ids = workspace.Entities.Where(e => SceneVideoPromptBuilder.NamesEntity(intent, e))
            .Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var previousShots = state.Clips.Take(completedClips ?? state.Current).Select(c => c.ShotId).ToHashSet(StringComparer.Ordinal);
        foreach (var shot in workspace.Prompts ?? [])
            if (previousShots.Contains(shot.Id)) ids.UnionWith(SceneVideoPromptBuilder.Participants(workspace, shot.Id).Select(e => e.Id));
        // Only an explicit continuation can opt into the previous story scene when no identity was named.
        if (ids.Count == 0 && (state.Input.StoryMode == "continue" || System.Text.RegularExpressions.Regex.IsMatch(intent, @"^\s*(?:continue|resume)\s+(?:(?:the|our)\s+)?(?:previous|last|story|scene)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) && workspace.Prompts?.LastOrDefault() is { } previous)
        {
            previousShots.Add(previous.Id);
            ids.UnionWith(SceneVideoPromptBuilder.Participants(workspace, previous.Id).Select(e => e.Id));
        }
        var facts = workspace.History.Where(f => !f.IsEvent).GroupBy(f => (f.EntityId, f.Property))
            .Select(g => g.OrderBy(f => f.Sequence).Last()).Where(f => !f.Removed).ToArray();
        SceneVideoPromptBuilder.ExpandPhysicalParticipants(ids, facts);
        return workspace with {
            Entities = workspace.Entities.Where(e => ids.Contains(e.Id)).ToArray(),
            History = workspace.History.Where(f => ids.Contains(f.EntityId) &&
                (f.RelatedEntityId == null || ids.Contains(f.RelatedEntityId)) &&
                (!f.IsEvent || (f.ParticipantIds ?? []).All(ids.Contains))).ToArray(),
            Prompts = workspace.Prompts?.Where(p => previousShots.Contains(p.Id)).Select(p => p with {
                DesignSnapshots = p.DesignSnapshots?.Where(e => ids.Contains(e.Id)).ToArray(),
                KnownEntityIds = p.KnownEntityIds?.Where(ids.Contains).ToArray(),
                IntroducedEntityIds = p.IntroducedEntityIds?.Where(ids.Contains).ToArray()
            }).ToArray()
        };
    }

    public static void Validate(SceneWorkspace workspace, SceneWorkspace scoped, ScenePromptPreview preview)
    {
        var allowed = scoped.Entities.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var entity in preview.Extraction.Entities)
            if (workspace.Entities.Any(e => !allowed.Contains(e.Id) && (e.Id == entity.Ref || e.Name.Equals(entity.Name, StringComparison.OrdinalIgnoreCase))))
                throw new InvalidOperationException("The interpretation introduced an existing identity outside this video's requested scene.");
        // Reject guessed references to hidden identities before committing against the complete workspace.
        ScenePromptReducer.Apply(scoped, preview with { Revision = ScenePromptReducer.Revision(scoped) });
    }
}
