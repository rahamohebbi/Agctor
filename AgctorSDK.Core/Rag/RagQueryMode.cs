namespace AgctorSDK.Core.Rag;

/// <summary>Normalized retrieval mode sent to external RAG backends (PRD-025).</summary>
public enum RagQueryMode
{
    /// <summary>Let the provider decide (LightRAG hybrid, Cognee FEELING_LUCKY equivalent).</summary>
    Auto = 0,

    /// <summary>Vector / chunk similarity only.</summary>
    Vector = 1,

    /// <summary>Knowledge-graph or memory-graph traversal.</summary>
    Graph = 2,

    /// <summary>Vector + graph / hybrid fusion.</summary>
    Hybrid = 3
}
