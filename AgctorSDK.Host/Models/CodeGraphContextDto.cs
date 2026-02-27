using AgctorSDK.CodeGraph.Persistence;

namespace AgctorSDK.Host.Models;

/// <summary>
/// CodeGraph context for dashboard: actor tree and embedding store summary (PRD-006).
/// Set by CodeGraphDemoScenario when the scenario is run.
/// </summary>
public class CodeGraphContextDto
{
    /// <summary>Root of the actor tree (Solution → Project → File → Class → Method).</summary>
    public ActorSerializer.ActorDto? ActorTree { get; set; }

    /// <summary>Summary of the embedding store (e.g. vector count).</summary>
    public EmbeddingStoreSummaryDto? EmbeddingStoreSummary { get; set; }
}

/// <summary>
/// Embedding store summary for dashboard display.
/// </summary>
public class EmbeddingStoreSummaryDto
{
    public int VectorCount { get; set; }
}
