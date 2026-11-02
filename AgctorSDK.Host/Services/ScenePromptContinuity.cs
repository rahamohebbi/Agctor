using System.Text.Json;
using System.Text.Json.Nodes;
using AgctorSDK.Core.SceneMemory;

namespace AgctorSDK.Host.Services;

/// <summary>Require an explicit decision for every current fact, including custom/legacy relationship names.</summary>
internal sealed class ScenePromptContinuity(SceneWorkspace workspace)
{
    private readonly Dictionary<string, SceneFact> _facts = workspace.History.Where(f => !f.IsEvent)
        .GroupBy(f => (f.EntityId, f.Property)).Select(g => g.Last()).Where(f => !f.Removed)
        .Select((f, i) => (Key: $"fact_{i + 1}", Fact: f)).ToDictionary(p => p.Key, p => p.Fact);

    internal JsonNode DescribeFacts()
    {
        var names = workspace.Entities.ToDictionary(e => e.Id, e => e.Name);
        return JsonSerializer.SerializeToNode(_facts.Select(p => new {
            factId = p.Key, entity = names.GetValueOrDefault(p.Value.EntityId, p.Value.EntityId), property = p.Value.Property,
            value = p.Value.Value, target = p.Value.RelatedEntityId == null ? "" : names.GetValueOrDefault(p.Value.RelatedEntityId, p.Value.RelatedEntityId)
        }))!;
    }

    internal void AddSchema(JsonNode schema, IEnumerable<string> sourceIds)
    {
        schema["properties"]!["entities"]!["maxItems"] = 0;
        schema["properties"]!["events"]!["maxItems"] = 0;
        var fields = new JsonObject();
        foreach (var key in _facts.Keys)
        {
            var alternatives = new JsonArray();
            foreach (var decision in new[] { "keep", "remove", "value", "target" })
            {
                // Reason first: the model must consider the evidence before choosing an operation.
                var properties = new JsonObject {
                    ["reason"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 240 },
                    ["decision"] = new JsonObject { ["const"] = decision }
                };
                var required = new List<string> { "reason", "decision" };
                if (decision != "keep")
                {
                    properties["evidence"] = new JsonObject { ["type"] = "string", ["enum"] = JsonSerializer.SerializeToNode(sourceIds) };
                    required.Add("evidence");
                }
                if (decision is "value" or "target")
                {
                    properties[decision] = new JsonObject { ["type"] = "string", ["minLength"] = 1 };
                    required.Add(decision);
                }
                alternatives.Add(new JsonObject { ["type"] = "object", ["additionalProperties"] = false,
                    ["properties"] = properties, ["required"] = JsonSerializer.SerializeToNode(required) });
            }
            fields[key] = new JsonObject { ["oneOf"] = alternatives };
        }
        schema["properties"]!["factReviews"] = new JsonObject { ["type"] = "object",
            ["additionalProperties"] = false, ["properties"] = fields,
            ["required"] = JsonSerializer.SerializeToNode(_facts.Keys) };
        schema["required"]!.AsArray().Add("factReviews");
    }

    // Subject IDs in plan must already be resolved. Review keys map to the precise saved property;
    // the model cannot accidentally remove a newly invented spelling instead of the old fact.
    internal ScenePromptExtraction Attach(string content, ScenePromptExtraction plan)
    {
        if (plan.Entities == null || plan.Changes == null || plan.Questions == null)
            throw new ArgumentException("The continuity check requires entities, changes and questions arrays.");
        var reviews = JsonNode.Parse(content)?["factReviews"] as JsonObject
            ?? throw new ArgumentException("The continuity check requires factReviews for every current fact.");
        if (reviews.Count != _facts.Count || reviews.Any(p => !_facts.ContainsKey(p.Key)))
            throw new ArgumentException("Review every current fact exactly once, using only the supplied fact IDs.");
        var existing = _facts.Values.Select(f => (f.EntityId, f.Property)).ToHashSet();
        if (plan.Changes.Any(c => c != null && existing.Contains((c.Subject, c.Property))))
            throw new ArgumentException("Correct existing facts through factReviews. The changes array is only for missing new properties.");
        var corrections = new List<ScenePromptChange>();
        string Text(JsonNode? node, string field, string key) =>
            node is JsonObject obj && obj[field] is JsonValue v && v.TryGetValue<string>(out var value) && !string.IsNullOrWhiteSpace(value)
                ? value : throw new ArgumentException($"factReviews.{key} requires a nonempty {field}.");
        foreach (var (key, fact) in _facts)
        {
            var review = reviews[key];
            _ = Text(review, "reason", key);
            var decision = Text(review, "decision", key);
            if (decision == "keep") continue;
            if (decision is not ("remove" or "value" or "target"))
                throw new ArgumentException($"Unknown continuity decision for {key}.");
            corrections.Add(new(fact.EntityId, fact.Property,
                decision == "value" ? Text(review, "value", key) : "",
                decision == "target" ? Text(review, "target", key) : "",
                decision == "remove", Text(review, "evidence", key)));
        }
        return plan with { Changes = corrections.Concat(plan.Changes).ToArray() };
    }
}
