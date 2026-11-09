using System.Text;
using System.Text.RegularExpressions;
using AgctorSDK.Core.SceneMemory;
using AgctorSDK.Host.Models.SceneMemory;

namespace AgctorSDK.Host.Services;

/// <summary>Deterministic, inspectable handoff from approved story state to a video composer.</summary>
public static class SceneVideoPromptBuilder
{
    private static SceneFact[] State(SceneWorkspace workspace, long sequence) => workspace.History
        .Where(f => !f.IsEvent && f.Sequence <= sequence)
        .GroupBy(f => (f.EntityId, Property: f.Property.ToLowerInvariant()))
        .Select(g => g.OrderBy(f => f.Sequence).Last()).Where(f => !f.Removed)
        .OrderBy(f => f.EntityId, StringComparer.Ordinal).ThenBy(f => f.Property, StringComparer.Ordinal).ToArray();

    public static SceneContinuityResponse Context(SceneWorkspace workspace) => new(workspace.Id,
        ScenePromptReducer.Revision(workspace), workspace.Entities, State(workspace, long.MaxValue),
        workspace.Prompts?.LastOrDefault()?.Id, workspace.Prompts?.LastOrDefault()?.Label);

    public static SceneEntity[] Participants(SceneWorkspace workspace, string shotId)
    {
        var shot = workspace.Prompts?.SingleOrDefault(p => p.Id == shotId)
            ?? throw new KeyNotFoundException("Shot not found in this story.");
        // A story link makes a design available; it does not cast that design in every shot.
        // Late links can still supply a design when the saved action actually names it.
        var membership = (shot.DesignSnapshots?.Select(e => e.Id) ?? shot.KnownEntityIds)?.ToHashSet(StringComparer.Ordinal);
        bool Mentioned(SceneEntity entity) => NamesEntity(shot.Prompt, entity);
        var shotEntities = workspace.Entities.Where(e =>
            (membership != null ? membership.Contains(e.Id) : e.CreatedAtSequence == null || e.CreatedAtSequence <= shot.LastSequence)
            || e.Studio != null).ToArray();
        var before = State(workspace, shot.FirstSequence - 1);
        var after = State(workspace, shot.LastSequence);
        // A model replaying earlier facts/events is not evidence of a new participant.
        var changes = workspace.History.Where(f => f.PromptId == shotId).Where(f => f.IsEvent
            ? !workspace.History.Any(old => old.IsEvent && old.Sequence < shot.FirstSequence &&
                old.Value == f.Value && old.EventMode == f.EventMode &&
                (old.ParticipantIds ?? []).OrderBy(id => id).SequenceEqual((f.ParticipantIds ?? []).OrderBy(id => id)))
            : !before.Any(old => old.EntityId == f.EntityId && old.Property.Equals(f.Property, StringComparison.OrdinalIgnoreCase) &&
                old.Value == f.Value && old.RelatedEntityId == f.RelatedEntityId && old.Removed == f.Removed)).ToArray();
        var relevant = changes.Select(f => f.EntityId).Concat(shot.IntroducedEntityIds ?? [])
            .Concat(changes.SelectMany(f => f.ParticipantIds ?? []))
            .Concat(changes.Where(f => f.RelatedEntityId != null).Select(f => f.RelatedEntityId!)).ToHashSet(StringComparer.Ordinal);
        foreach (var entity in shotEntities)
            if (Mentioned(entity)) relevant.Add(entity.Id);
        // An explicit continuation with no new participants can reuse the previous
        // shot's cast within the same scene, never the whole story/library snapshot.
        if (relevant.Count == 0)
        {
            var previous = workspace.Prompts!.TakeWhile(p => p.Id != shotId).LastOrDefault();
            if (previous != null && previous.SceneLabel == shot.SceneLabel &&
                IsContinuation(shot.Prompt))
                relevant.UnionWith(Participants(workspace, previous.Id).Select(e => e.Id));
        }
        // Follow physical continuity, not the entire social/ownership graph or everyone
        // sharing a location. Naming a forest must not import all its other occupants.
        ExpandPhysicalParticipants(relevant, before.Concat(after));
        var entities = shotEntities.Where(e => relevant.Contains(e.Id)).ToArray();
        return entities;
    }

    internal static void ExpandPhysicalParticipants(HashSet<string> relevant, IEnumerable<SceneFact> source)
    {
        var facts = source.ToArray();
        bool added;
        do
        {
            added = false;
            foreach (var fact in facts.Where(f => f.RelatedEntityId != null))
            {
                var property = fact.Property.ToLowerInvariant();
                if (relevant.Contains(fact.EntityId) && property is
                    "held_by" or "worn_by" or "carried_by" or "in" or "on" or "located_at" or
                    "stuck_in" or "restrained_by" or "facing" or "beside" or "protecting" or "restrains")
                    added |= relevant.Add(fact.RelatedEntityId!);
                if (relevant.Contains(fact.RelatedEntityId!) && property is "held_by" or "worn_by" or "carried_by")
                    added |= relevant.Add(fact.EntityId);
            }
        } while (added);
    }

    internal static bool NamesEntity(string prompt, SceneEntity entity) => Mentions(prompt, entity.Name, entity.Kind)
        || (entity.Studio != null && Mentions(prompt, entity.Studio.DisplayName, entity.Kind));

    internal static bool IsContinuation(string prompt) => Regex.IsMatch(prompt,
        @"\b(?:continu(?:e|es|ing)|same|she|he|they|it|them|her|him|hold (?:this|that|the) pose)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool Mentions(string prompt, string name, string kind)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        name = name.Trim();
        // "Woman in red coat" also matches "the woman in the red coat".
        if (name.Contains(' '))
        {
            const string articles = @"\b(?:a|an|the)\s+";
            name = Regex.Replace(name, articles, "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            prompt = Regex.Replace(prompt, articles, "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        var forms = new List<string> { name };
        // Whole-word, conservative English inflections cover "Sword" / "swords",
        // "Box" / "boxes" without matching "swordfish" or expanding character names.
        if (kind is "Object" or "Location")
        {
            if (Regex.IsMatch(name, @"[^aeiou]y$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                forms.Add(name[..^1] + "ies");
            else if (Regex.IsMatch(name, @"(?:s|x|z|ch|sh)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                forms.Add(name + "es");
            else forms.Add(name + "s");
        }
        return forms.Any(form => Regex.IsMatch(prompt,
            @"(?<![\p{L}\p{N}_])" + Regex.Escape(form) + @"(?![\p{L}\p{N}_])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    }

    public static SceneVideoPromptResponse Prepare(SceneWorkspace workspace, string shotId, string expectedRevision)
    {
        var revision = ScenePromptReducer.Revision(workspace);
        if (expectedRevision != revision)
            throw new InvalidOperationException("The story or linked designs changed. Refresh the timeline before preparing this shot.");
        var shot = workspace.Prompts?.SingleOrDefault(p => p.Id == shotId)
            ?? throw new KeyNotFoundException("Apply this shot to the story before preparing its video prompt.");
        var entities = Participants(workspace, shotId);
        var relevant = entities.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var before = State(workspace, shot.FirstSequence - 1);
        var after = State(workspace, shot.LastSequence);
        before = before.Where(f => relevant.Contains(f.EntityId) && (f.RelatedEntityId == null || relevant.Contains(f.RelatedEntityId))).ToArray();
        after = after.Where(f => relevant.Contains(f.EntityId) && (f.RelatedEntityId == null || relevant.Contains(f.RelatedEntityId))).ToArray();
        var names = workspace.Entities.ToDictionary(e => e.Id, e => e.Name);
        string Name(string id) => names.GetValueOrDefault(id, id);
        string Fact(SceneFact f) => $"{Name(f.EntityId)} — {f.Property.Replace('_', ' ')}: {(f.RelatedEntityId is null ? f.Value : Name(f.RelatedEntityId))}";
        var text = new StringBuilder();
        text.AppendLine("SHOT ACTION").AppendLine(shot.Prompt.Trim()).AppendLine();
        text.AppendLine("CONTINUITY GUIDANCE")
            .AppendLine("Follow the shot action in order. Starting conditions apply before the action; ending conditions apply after it. Do not depict both states simultaneously. Saved story facts take precedence over design references. Unrecorded details are unspecified.")
            .AppendLine("These are story intentions, not observations of generated video. Text descriptions do not supply reference images.").AppendLine();
        text.AppendLine("SHOT DESIGN REFERENCES (participating designs only)");
        foreach (var entity in entities.Where(e => e.Studio != null))
        {
            text.AppendLine($"{entity.Name} ({entity.Kind}; design: {entity.Studio!.DisplayName})");
            foreach (var pair in entity.Studio.Defaults.Where(p => !p.Key.StartsWith("cynelo_", StringComparison.Ordinal)).OrderBy(p => p.Key, StringComparer.Ordinal))
                text.AppendLine($"- {pair.Key.Replace("design_", "").Replace('_', ' ')}: {pair.Value}");
        }
        if (entities.All(e => e.Studio == null)) text.AppendLine("No linked designs for the participants in this shot.");
        text.AppendLine().AppendLine("START OF SHOT — saved state before the action");
        foreach (var fact in before) text.AppendLine("- " + Fact(fact));
        if (before.Length == 0) text.AppendLine("No earlier state recorded for these participants.");
        text.AppendLine().AppendLine("END OF SHOT — approved resulting state");
        foreach (var fact in after) text.AppendLine("- " + Fact(fact));
        // Preserve removals; absence alone must not resurrect a prior trapped/held state.
        var finalKeys = after.Select(f => (f.EntityId, f.Property.ToLowerInvariant())).ToHashSet();
        foreach (var fact in before.Where(f => !finalKeys.Contains((f.EntityId, f.Property.ToLowerInvariant()))))
            text.AppendLine($"- {Name(fact.EntityId)} — {fact.Property.Replace('_', ' ')}: cleared in this shot (was {fact.Value}).");
        if (after.Length == 0 && before.Length == 0) text.AppendLine("No additional state constraints recorded; follow the approved shot action.");
        return new(workspace.Id, revision, shot.Id, shot.Label, SceneShotDuration.Resolve(shot.DurationSeconds, shot.Prompt), text.ToString().Trim(), entities, before, after,
            ["Uses the latest linked designs for this shot's participants. Story action and historical facts stay unchanged.", "Reference images and video generation settings are selected separately in Cynelo."]);
    }
}
