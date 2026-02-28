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

/// <summary>
/// Single embedding record for debugging/visualization (e.g. GET /api/CodeGraph/embeddings).
/// </summary>
public class EmbeddingRecordDto
{
    public string ActorId { get; set; } = null!;
    public string Text { get; set; } = null!;
    public int VectorLength { get; set; }
    public float[] Vector { get; set; } = null!;
}
