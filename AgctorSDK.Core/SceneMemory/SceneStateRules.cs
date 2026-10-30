using System;
using System.Collections.Generic;
using System.Linq;

namespace AgctorSDK.Core.SceneMemory;

/// <summary>A small semantic vocabulary with open-ended attributes. These rules operate on interpreted facts, never raw prose.</summary>
public static class SceneStateRules
{
    public static bool ReleasesObject(string property, string value) => property == "disposition" &&
        value is "discarded" or "lost";

    public static bool IsActivity(string property) => property is "activity" or "action" || property.StartsWith("activity_", StringComparison.Ordinal);

    public static ScenePromptChange Normalize(ScenePromptChange change, SceneEntity entity)
    {
        if (change.Property == null || change.Value == null) return change;
        var value = change.Value.Trim();
        var lower = value.ToLowerInvariant();
        var property = change.Property;
        if (entity.Kind == "Character" && property is "status" or "condition" or "health" && lower is "dead" or "deceased" or "alive" or "undead")
            return change with { Property = "life_state", Value = lower == "deceased" ? "dead" : lower };
        if (entity.Kind == "Character" && property is "status" or "condition" && lower is "sick" or "ill" or "healthy")
            return change with { Property = "health", Value = lower == "ill" ? "sick" : lower };
        if (entity.Kind == "Object" && property is "status" or "disposition" && lower is "discarded" or "thrown_away" or "thrown away" or "lost")
            return change with { Property = "disposition", Value = lower == "lost" ? "lost" : "discarded" };
        if (property.StartsWith("can_", StringComparison.Ordinal)) property = "ability_" + property.Substring(4);
        if (property.StartsWith("capability_", StringComparison.Ordinal)) property = "ability_" + property.Substring(11);
        if (property is "life_state" or "health" or "disposition" or "status") value = lower;
        return change with { Property = property, Value = value };
    }

    public static void Validate(ScenePromptChange change, SceneEntity subject, SceneEntity? target,
        IReadOnlyDictionary<(string EntityId, string Property), SceneFact> current)
    {
        if (change.Remove) return;
        if (change.Property is "ability" or "abilities" or "skill" or "skills" or "capabilities")
            throw new ArgumentException("Store abilities separately as ability_<base_verb>, for example ability_sing=true and ability_dance=true. Temporary health or availability does not erase learned skills.");
        if ((change.Property.StartsWith("ability_", StringComparison.Ordinal) || change.Property.StartsWith("availability_", StringComparison.Ordinal)) && target != null)
            throw new ArgumentException("An ability or its availability needs a text value, not a target entity.");
        if (change.Property == "life_state" && (subject.Kind != "Character" || target != null))
            throw new ArgumentException("life_state is a character attribute (for example alive, dead or undead).");
        if (change.Property == "disposition" && (subject.Kind != "Object" || target != null))
            throw new ArgumentException("disposition is an object attribute (for example discarded, lost or recovered).");
        if (IsActivity(change.Property) && current.TryGetValue((subject.Id, "life_state"), out var life) && !life.Removed && life.Value == "dead")
            throw new ArgumentException("A dead character cannot begin a new ongoing activity without an explicit new life state. Record past events as events, or clarify revival/undead activity first.");
    }
}
