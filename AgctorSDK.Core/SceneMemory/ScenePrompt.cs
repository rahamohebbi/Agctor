using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AgctorSDK.Core.SceneMemory;

/// <summary>Model output uses existing entity IDs or local references for newly discovered entities.</summary>
public sealed record ScenePromptEntity(string Ref, string Name, string Kind);
public sealed record ScenePromptChange(string Subject, string Property, string Value, string Target, bool Remove, string Evidence);
public sealed record ScenePromptEvent(string Description, IReadOnlyList<string> Participants, string Evidence, string Mode = "occurred");
public sealed record ScenePromptExtraction(IReadOnlyList<ScenePromptEntity> Entities,
    IReadOnlyList<ScenePromptChange> Changes, IReadOnlyList<string> Questions, IReadOnlyList<ScenePromptEvent>? Events = null);
public sealed record ScenePromptPreview(string Id, string Revision, string Prompt, string Label, string Model,
    ScenePromptExtraction Extraction, string SceneLabel = "", double? DurationSeconds = null);
public sealed record ScenePromptRecord(string Id, string Prompt, string Label, string Model,
    long FirstSequence, long LastSequence, DateTimeOffset RecordedAt, string SceneLabel = "", double? DurationSeconds = null,
    IReadOnlyList<string>? IntroducedEntityIds = null, IReadOnlyList<SceneEntity>? DesignSnapshots = null,
    IReadOnlyList<string>? KnownEntityIds = null);

/// <summary>Shared validation and state reduction for preview and commit. No model output writes directly to storage.</summary>
public static class ScenePromptReducer
{
    private static readonly string[] Kinds = { "Character", "Object", "Location", "Audio", "Music", "Other" };
    private static readonly string[] Placement = { "held_by", "on", "in", "located_at" };
    private static readonly string[] PossessionAliases = { "has", "holds", "holding", "carries", "carrying" };
    public static bool IsTerminalStatus(string property, string value) => property == "status" &&
        (string.Equals(value, "consumed", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "destroyed", StringComparison.OrdinalIgnoreCase));
    public static string Revision(SceneWorkspace workspace) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(workspace))));

    public static SceneWorkspace Apply(SceneWorkspace workspace, ScenePromptPreview preview, bool allowQuestions = false)
    {
        if (workspace.DeletedShotIds?.Contains(preview.Id) == true)
            throw new InvalidOperationException("This shot was deleted. Preview it again to create a new shot.");
        if (workspace.Prompts?.Any(p => p.Id == preview.Id) == true) return workspace;
        if (preview.Revision != Revision(workspace))
            throw new InvalidOperationException("Scene Memory changed since this preview. Interpret the prompt again before applying it.");
        var plan = preview.Extraction ?? throw new ArgumentException("Missing scene changes.");
        if (string.IsNullOrWhiteSpace(preview.Id) || string.IsNullOrWhiteSpace(preview.Prompt) || preview.Prompt.Length > 12000
            || preview.Label.Length > 100 || preview.SceneLabel.Length > 100 || string.IsNullOrWhiteSpace(preview.Model)
            || preview.DurationSeconds is <= 0 or > 3600)
            throw new ArgumentException("Invalid prompt metadata.");
        if (plan.Entities == null || plan.Changes == null || plan.Questions == null || plan.Entities.Count > 40
            || plan.Changes.Count > 150 || plan.Questions.Count > 20 || (plan.Events?.Count ?? 0) > 80 || plan.Questions.Any(q => string.IsNullOrWhiteSpace(q) || q.Length > 1000))
            throw new ArgumentException("The model returned an invalid or oversized scene plan. Try a shorter prompt.");
        if (plan.Questions.Count > 0 && !allowQuestions)
            throw new InvalidOperationException("Clarify the questions in your prompt and interpret it again. No changes were saved.");

        var entities = workspace.Entities.ToList();
        var references = entities.ToDictionary(e => e.Id);
        var declared = new HashSet<string>(StringComparer.Ordinal);
        for (var entityIndex = 0; entityIndex < plan.Entities.Count; entityIndex++)
        {
            var proposed = plan.Entities[entityIndex];
            try
            {
            if (proposed == null) throw new ArgumentException("The entity declaration is empty.");
            if (string.IsNullOrWhiteSpace(proposed.Ref) || proposed.Ref.Length > 100) throw new ArgumentException("The entity reference must contain 1–100 characters.");
            if (string.IsNullOrWhiteSpace(proposed.Name) || proposed.Name.Length > 100) throw new ArgumentException("The entity name must contain 1–100 characters.");
            if (!Kinds.Contains(proposed.Kind)) throw new ArgumentException($"Unknown entity kind '{proposed.Kind}'. Use Character, Object, Location, Audio, Music or Other.");
            if (!declared.Add(proposed.Ref)) throw new ArgumentException($"Entity reference '{proposed.Ref}' is declared more than once. Each declaration needs a unique reference.");
            if (references.TryGetValue(proposed.Ref, out var existing))
            {
                if (!string.Equals(existing.Name, proposed.Name.Trim(), StringComparison.OrdinalIgnoreCase) || existing.Kind != proposed.Kind)
                    throw new ArgumentException("The model attempted to change an existing identity. Clarify the prompt and try again.");
                continue;
            }
            // Local refs identify separate newly discovered entities. Equal display names
            // (e.g. two matching swords) must not silently collapse them into one object.
            existing = workspace.Entities.FirstOrDefault(e => string.Equals(e.Name, proposed.Name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (existing != null && existing.Kind != proposed.Kind)
                throw new ArgumentException("An entity with this name already exists with a different type.");
            if (existing != null && plan.Entities.Any(e => e != null && e.Ref != proposed.Ref &&
                string.Equals(e.Name?.Trim(), proposed.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"Multiple references use the saved name '{existing.Name}'. Reuse its existing ID for the same entity; give a different item its own distinct name and reference.");
            var name = proposed.Name.Trim();
            for (var number = 2; existing == null && entities.Any(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)); number++)
            {
                var suffix = $" ({number})";
                name = proposed.Name.Trim()[..Math.Min(proposed.Name.Trim().Length, 100 - suffix.Length)] + suffix;
            }
            var entity = existing ?? new SceneEntity(Guid.NewGuid().ToString("N"), name, proposed.Kind);
            if (existing == null) entities.Add(entity);
            references.Add(proposed.Ref, entity);
            }
            catch (ArgumentException ex) when (ex is not ScenePromptValidationException)
            {
                throw new ScenePromptValidationException(new[] { ScenePromptDiagnostics.Issue(preview.Prompt,
                    "invalid_entity", $"entities[{entityIndex}]", ex.Message, proposed?.Ref, value: proposed?.Name,
                    suggestion: "Correct this entity declaration. Preserve existing IDs and use unique references for new entities.") });
            }
        }

        ScenePromptDiagnostics.ValidateFields(plan, references, preview.Prompt);
        var history = workspace.History.ToList();
        var current = history.Where(f => !f.IsEvent).GroupBy(f => (f.EntityId, f.Property)).ToDictionary(g => g.Key, g => g.Last());
        void Append(SceneEntity subject, string property, string value, string? target, bool removed, string evidence)
        {
            var fact = new SceneFact(history.Count + 1L, subject.Id, property, value, evidence,
                DateTimeOffset.UtcNow, target, removed, preview.Id);
            history.Add(fact); current[(subject.Id, property)] = fact;
        }
        for (var changeIndex = 0; changeIndex < plan.Changes.Count; changeIndex++)
        {
            var sourceChange = plan.Changes[changeIndex];
            try
            {
            var change = sourceChange;
            var subject = references[change.Subject];
            change = SceneStateRules.Normalize(change, subject);
            SceneEntity? target = null;
            if (!change.Remove || PossessionAliases.Contains(change.Property) || change.Property == "owns")
            {
                // Structured models sometimes put an entity ID in value instead of target.
                // For typed relations only, resolve that exact reference; never infer an unknown entity.
                var targetReference = change.Target;
                if (targetReference.Length == 0 && (Placement.Contains(change.Property) || PossessionAliases.Contains(change.Property) || change.Property is "consumed_by" or "destroyed_by" or "owned_by" or "owns"))
                    targetReference = change.Value;
                if (targetReference.Length > 0 && !references.TryGetValue(targetReference, out target))
                {
                    if (plan.Entities.Count(e => string.Equals(e.Name.Trim(), targetReference, StringComparison.OrdinalIgnoreCase)) > 1)
                        throw new ArgumentException($"The relationship name '{targetReference}' identifies multiple entities. Use the specific declared reference for its target.");
                    target = entities.SingleOrDefault(e => string.Equals(e.Name, targetReference, StringComparison.OrdinalIgnoreCase))
                        ?? throw new ArgumentException(change.Property is "owned_by" or "owns"
                            ? "Ownership needs two known entities. Discover the owned object or location in entities first, then link item owned_by owner using target, not a plain-text object name on the owner."
                            : "A relationship refers to an unknown entity.");
                }
            }
            if (change.Property == "owns")
            {
                if (subject.Kind is not ("Character" or "Other") || target == null || target.Kind is not ("Object" or "Location" or "Other"))
                    throw new ArgumentException("Ownership must identify an owner and a specific object or location. Discover the item first and use item owned_by owner.");
                var owner = subject;
                if (change.Remove && current.TryGetValue((target.Id, "owned_by"), out var recordedOwner) &&
                    !recordedOwner.Removed && recordedOwner.RelatedEntityId != owner.Id)
                    throw new ArgumentException("This entity is not the item's current owner. Do not clear another owner's relationship.");
                subject = target;
                target = change.Remove ? null : owner;
                change = change with { Subject = subject.Id, Property = "owned_by", Value = "", Target = target?.Id ?? "" };
            }
            if (!change.Remove && change.Property == "owned_by" &&
                (subject.Kind is not ("Object" or "Location" or "Other") || target == null || target.Kind is not ("Character" or "Other") || target.Id == subject.Id))
                throw new ArgumentException("Ownership must link an object or location to its owner: item owned_by owner. Discover the item in entities first; do not store its name as a text property on the owner.");
            if (PossessionAliases.Contains(change.Property))
            {
                if (subject.Kind != "Character" || target?.Kind != "Object")
                    throw new ArgumentException("Possession must identify the character and the specific object. Use object held_by character; use owned_by for ownership without physical possession.");
                // Store possession only on the object, even when a model describes it from the character's side.
                var holder = subject;
                if (change.Remove && current.TryGetValue((target.Id, "held_by"), out var recordedHolder) &&
                    !recordedHolder.Removed && recordedHolder.RelatedEntityId != holder.Id)
                    throw new ArgumentException("This character is not the object's current holder. Do not clear another character's possession.");
                subject = target;
                target = change.Remove ? null : holder;
                change = change with { Subject = subject.Id, Property = "held_by", Value = "", Target = target?.Id ?? "" };
            }
            if (!change.Remove && change.Property == "action" && (string.IsNullOrWhiteSpace(change.Value) ||
                references.ContainsKey(change.Value.Trim()) ||
                entities.Any(e => string.Equals(e.Name, change.Value.Trim(), StringComparison.OrdinalIgnoreCase))))
                throw new ArgumentException($"Invalid action for '{subject.Name}': value='{change.Value}', target='{target?.Name ?? change.Target}'. Source: \"{change.Evidence}\". " +
                    "An action must describe what happened, not just name its object. Use a descriptive event and record any resulting state on the affected entities. " +
                    "Use typed relationships for ongoing facing, protecting or restraint. For completed eating use status=consumed and consumed_by=character; distinguish ongoing or partial actions.");
            SceneStateRules.Validate(change, subject, target, current);
            // Some models echo the previous value/target when clearing a property. A removal
            // always writes an empty tombstone; echoed data must never reintroduce the relation.
            if (!change.Remove && target == null && string.IsNullOrWhiteSpace(change.Value))
                throw new ArgumentException($"Empty change for '{subject.Name}', property='{change.Property}', source=\"{change.Evidence}\". " +
                    "A change needs a value or a target entity. Supply the explicitly stated value/relationship, or use remove=true only when the prompt ends an existing fact. Do not invent a value.");
            if (!change.Remove && Placement.Contains(change.Property) && (target == null || target.Id == subject.Id))
                throw new ArgumentException("A placement must refer to a different entity.");
            if (!change.Remove && change.Property == "held_by" && (subject.Kind != "Object" || target?.Kind != "Character"))
                throw new ArgumentException($"Invalid possession: '{subject.Name}' ({subject.Kind}) held_by '{target?.Name ?? change.Target}' ({target?.Kind ?? "unknown"}). " +
                    $"Source: \"{change.Evidence}\". held_by requires subject=Object and target=Character. " +
                    "Check the relationship direction and the declared entity types against the prompt. People, animal characters and acting monsters are Characters; props are Objects. " +
                    "For characters trapped by slime use character stuck_in slime, not held_by. Give each character their own object when the prompt describes separate matching props. " +
                    "Correct newly discovered entities when needed; do not change existing identities or invent possession.");
            if (!change.Remove && change.Property is "consumed_by" or "destroyed_by" &&
                ((subject.Kind != "Object" && !(subject.Kind == "Location" && change.Property == "destroyed_by")) || target?.Kind != "Character"))
                throw new ArgumentException("Consumption/destruction provenance must link an object (or destroyed location) to a character.");
            if (!change.Remove && change.Property == "held_by" && current.TryGetValue((subject.Id, "status"), out var status)
                && !status.Removed && IsTerminalStatus(status.Property, status.Value))
                throw new ArgumentException("A consumed or destroyed object cannot be held. Explicitly restore its status first, or introduce a different object.");
            if (!change.Remove && IsTerminalStatus(change.Property, change.Value))
            {
                if ((subject.Kind != "Object" && !(subject.Kind == "Location" && change.Value == "destroyed")) || target != null)
                    throw new ArgumentException("Consumed status applies to objects; destroyed status applies to objects or locations. Use life_state for characters.");
                // An exhausted/destroyed object remains in history, but is no longer physically held or placed.
                foreach (var property in subject.Kind == "Object" ? Placement.Append("carried_at") : Array.Empty<string>())
                    if (current.TryGetValue((subject.Id, property), out var old) && !old.Removed)
                        Append(subject, property, "", null, true, change.Evidence);
                // Retire legacy action=object entries that omitted the verb and left dangling activity.
                foreach (var old in current.Values.Where(f => f.Property == "action" && !f.Removed &&
                    (f.RelatedEntityId == subject.Id || string.Equals(f.Value, subject.Name, StringComparison.OrdinalIgnoreCase))).ToArray())
                    Append(entities.Single(e => e.Id == old.EntityId), old.Property, "", null, true, change.Evidence);
            }
            if (!change.Remove && SceneStateRules.ReleasesObject(change.Property, change.Value))
            {
                // Discarding/losing ends possession and prior placement, but does not destroy the object.
                // An explicitly described destination can be set by a subsequent change.
                foreach (var property in Placement.Append("carried_at"))
                    if (current.TryGetValue((subject.Id, property), out var old) && !old.Removed)
                        Append(subject, property, "", null, true, change.Evidence);
            }
            if (!change.Remove && change.Property == "life_state" && change.Value == "dead")
                foreach (var old in current.Values.Where(f => f.EntityId == subject.Id && !f.Removed && SceneStateRules.IsActivity(f.Property)).ToArray())
                    Append(subject, old.Property, "", null, true, change.Evidence);
            if (!change.Remove && Placement.Contains(change.Property))
            {
                if (change.Property == "held_by" && current.TryGetValue((subject.Id, "disposition"), out var disposition) &&
                    !disposition.Removed && SceneStateRules.ReleasesObject(disposition.Property, disposition.Value))
                    Append(subject, "disposition", "recovered", null, false, change.Evidence);
                var sameHolder = change.Property == "held_by" && current.TryGetValue((subject.Id, "held_by"), out var previousHolder)
                    && !previousHolder.Removed && previousHolder.RelatedEntityId == target?.Id;
                if (!sameHolder && current.TryGetValue((subject.Id, "carried_at"), out var carriedAt) && !carriedAt.Removed)
                    Append(subject, "carried_at", "", null, true, change.Evidence);
                // A pickup/transfer/placement supersedes the previous direct physical placement.
                foreach (var property in Placement.Where(p => p != change.Property))
                    if (current.TryGetValue((subject.Id, property), out var old) && !old.Removed)
                        Append(subject, property, "", null, true, change.Evidence);
                var visited = new HashSet<string> { subject.Id };
                var next = target?.Id;
                while (next != null)
                {
                    if (!visited.Add(next)) throw new ArgumentException("The model returned a circular physical placement.");
                    next = current.Values.FirstOrDefault(f => f.EntityId == next && !f.Removed && Placement.Contains(f.Property))?.RelatedEntityId;
                }
            }
            if (Placement.Contains(change.Property) || (!change.Remove && (IsTerminalStatus(change.Property, change.Value) || SceneStateRules.ReleasesObject(change.Property, change.Value))))
            {
                // Retire pre-normalization actor-side possession facts to avoid contradictory reverse lookups.
                foreach (var old in current.Values.Where(f => !f.Removed && PossessionAliases.Contains(f.Property) &&
                    (f.RelatedEntityId == subject.Id || string.Equals(f.Value, subject.Name, StringComparison.OrdinalIgnoreCase))).ToArray())
                    Append(entities.Single(e => e.Id == old.EntityId), old.Property, "", null, true, change.Evidence);
                if (change.Remove && change.Property == "held_by" && current.TryGetValue((subject.Id, "carried_at"), out var position) && !position.Removed)
                    Append(subject, "carried_at", "", null, true, change.Evidence);
            }
            // Descriptions/dialogue may also identify a participant. Only canonical relations
            // use the target name as their value; do not erase speech or action text.
            var relationValue = Placement.Contains(change.Property) || change.Property is "owned_by" or "consumed_by" or "destroyed_by";
            Append(subject, change.Property, change.Remove ? "" : target != null && (relationValue || string.IsNullOrWhiteSpace(change.Value)) ? target.Name : change.Value,
                change.Remove ? null : target?.Id, change.Remove, change.Evidence);
            }
            catch (ArgumentException ex) when (ex is not ScenePromptValidationException)
            {
                throw new ScenePromptValidationException(new[] { ScenePromptDiagnostics.Issue(preview.Prompt,
                    "invalid_relationship", $"changes[{changeIndex}]", ex.Message, sourceChange.Subject,
                    sourceChange.Property, sourceChange.Value, sourceChange.Target, sourceChange.Evidence) });
            }
        }
        foreach (var sceneEvent in plan.Events ?? Array.Empty<ScenePromptEvent>())
        {
            if (sceneEvent == null || string.IsNullOrWhiteSpace(sceneEvent.Description) || sceneEvent.Description.Length > 1000 ||
                sceneEvent.Participants == null || sceneEvent.Participants.Count == 0 || sceneEvent.Participants.Count > 20 ||
                string.IsNullOrWhiteSpace(sceneEvent.Evidence) || sceneEvent.Evidence.Length > 2000 ||
                !preview.Prompt.Contains(sceneEvent.Evidence, StringComparison.Ordinal) ||
                sceneEvent.Mode is not ("occurred" or "ongoing" or "intended" or "hypothetical" or "negated"))
                throw new ArgumentException($"Invalid event '{sceneEvent?.Description}': evidence='{sceneEvent?.Evidence}', mode='{sceneEvent?.Mode}'. " +
                    "Every event needs a description (1–1,000 characters), 1–20 participant references, an exact excerpt (1–2,000 characters) from the current prompt, " +
                    "and mode occurred, ongoing, intended, hypothetical or negated. Fix this event without dropping its meaning or inventing evidence.");
            var participants = sceneEvent.Participants.Select(reference => reference != null && references.TryGetValue(reference, out var entity)
                ? entity.Id : throw new ArgumentException($"Event '{sceneEvent.Description}' refers to unknown participant '{reference}'. Use a declared entity reference or existing entity ID.")).Distinct().ToArray();
            history.Add(new SceneFact(history.Count + 1L, participants[0], "event", sceneEvent.Description, sceneEvent.Evidence,
                DateTimeOffset.UtcNow, PromptId: preview.Id, IsEvent: true, ParticipantIds: participants, EventMode: sceneEvent.Mode));
        }
        foreach (var fact in current.Values.Where(f => !f.Removed && f.Property is "consumed_by" or "destroyed_by"))
        {
            var expected = fact.Property == "consumed_by" ? "consumed" : "destroyed";
            if (!current.TryGetValue((fact.EntityId, "status"), out var state) || state.Removed ||
                !string.Equals(state.Value, expected, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"The affected object needs status={expected}, not only an action relationship.");
        }
        var originalEntityIds = workspace.Entities.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var record = new ScenePromptRecord(preview.Id, preview.Prompt, preview.Label, preview.Model,
            workspace.History.Count + 1L, history.Count, DateTimeOffset.UtcNow, preview.SceneLabel,
            preview.DurationSeconds, entities.Where(e => !originalEntityIds.Contains(e.Id)).Select(e => e.Id).ToArray(),
            SceneShotDesigns.Capture(entities));
        return workspace with { Entities = entities.ToArray(), History = history.ToArray(),
            Prompts = (workspace.Prompts ?? Array.Empty<ScenePromptRecord>()).Append(record).ToArray() };
    }
}
