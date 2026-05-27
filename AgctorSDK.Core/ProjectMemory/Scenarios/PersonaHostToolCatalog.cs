using System;
using System.Collections.Generic;
using System.Linq;

namespace AgctorSDK.Core.ProjectMemory.Scenarios;

/// <summary>
/// Host HTTP tool ids eligible per project-memory persona (flow designer + Agent Studio).
/// Single server-side source — keep in sync with playground routing expectations.
/// </summary>
public static class PersonaHostToolCatalog
{
    public sealed record Def(string Id, string Group, IReadOnlyList<string> PersonaIds);

    /// <summary>Semantic YAML tokens that are not direct HTTP tool ids.</summary>
    public static IReadOnlyList<(string Id, string Label)> SemanticTokens { get; } =
        new List<(string, string)>
        {
            ("read_document", "Read documents"),
            ("write_document", "Write documents"),
            ("search_entities", "Search entities"),
            ("load_schema", "Load schema"),
            ("memory_intents_only", "Memory intents only")
        };

    public static IReadOnlyList<Def> All { get; } = new List<Def>
    {
        new(ScenarioFlowLlmNodeToolIds.PersonMemoryContext, "Memory",
            new[] { "person-query", "relationship-coach" }),
        new(ScenarioFlowLlmNodeToolIds.ApplyMemoryIntents, "Memory",
            new[] { "memory-curator" }),
        new(ScenarioFlowLlmNodeToolIds.PersonVisualContext, "Visual",
            new[] { "person-query", "relationship-coach", "style-coach", "fitness-coach" }),
        new(ScenarioFlowLlmNodeToolIds.PersonVisualIngest, "Visual",
            new[] { "visual-intake" }),
        new(ScenarioFlowLlmNodeToolIds.PersonVisualExtract, "Visual",
            new[] { "visual-intake" })
    };

    public static IReadOnlyList<Def> ForPersona(string personaId)
    {
        if (string.IsNullOrWhiteSpace(personaId))
            return Array.Empty<Def>();

        var pid = personaId.Trim();
        return All
            .Where(d => d.PersonaIds.Any(p => string.Equals(p, pid, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    public static IReadOnlyList<string> EligibleHostToolIds(string personaId) =>
        ForPersona(personaId).Select(d => d.Id).ToList();
}
