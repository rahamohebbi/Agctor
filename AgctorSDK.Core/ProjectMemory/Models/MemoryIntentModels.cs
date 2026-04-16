using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AgctorSDK.Core.ProjectMemory.Models;

/// <summary>
/// JSON envelope for extractor output (PRD §12.3).
/// </summary>
public sealed class MemoryIntentBatch
{
    /// <summary>Optional catalog id (e.g. "people") — curator uses <c>scenarios/&lt;id&gt;/people/</c> when set.</summary>
    [JsonPropertyName("scenarioId")]
    public string? ScenarioId { get; set; }

    [JsonPropertyName("memoryIntents")]
    public List<MemoryIntent> MemoryIntents { get; set; } = new();
}

public sealed class MemoryIntent
{
    [JsonPropertyName("entityKey")]
    public string EntityKey { get; set; } = "";

    [JsonPropertyName("knowledgeType")]
    public string KnowledgeType { get; set; } = "";

    [JsonPropertyName("attribute")]
    public string? Attribute { get; set; }

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }
}

/// <summary>Normalized intent after routing — ready for projection.</summary>
public sealed class RoutedMemoryIntent
{
    public MemoryIntent Original { get; init; } = new();
    public string DocumentTypeId { get; init; } = "";
    public string SectionTitle { get; init; } = "";
    public string UpdateMode { get; init; } = "replace_section";
    public string FileName { get; init; } = "";
}
