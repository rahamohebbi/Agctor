namespace AgctorSDK.Core.Rag;

/// <summary>Canonical provider ids shared by catalog, factory, and configuration.</summary>
public static class RagProviderIds
{
    public const string None = "None";
    public const string LightRag = "LightRAG";
    public const string Cognee = "Cognee";
    public const string Graphiti = "Graphiti";
    /// <summary>Scene-state catalog entry; accessed through ISceneMemoryStore rather than a document RAG adapter.</summary>
    public const string SceneMemory = "SceneMemory";

    /// <summary>Document retrieval adapter ids in display order. Scene Memory has a separate store/tool contract.</summary>
    public static readonly IReadOnlyList<string> All = new[] { None, LightRag, Graphiti, Cognee };

    /// <summary>Maps aliases to canonical ids; returns None when blank.</summary>
    public static string Normalize(string? providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
            return None;

        var t = providerId.Trim();
        if (t.Equals(SceneMemory, StringComparison.OrdinalIgnoreCase)
            || t.Equals("scene-memory", StringComparison.OrdinalIgnoreCase))
            return SceneMemory;
        if (t.Equals(None, StringComparison.OrdinalIgnoreCase)
            || t.Equals("MarkdownOnly", StringComparison.OrdinalIgnoreCase)
            || t.Equals("markdown_only", StringComparison.OrdinalIgnoreCase))
            return None;

        if (t.Equals(LightRag, StringComparison.OrdinalIgnoreCase)
            || t.Equals("lightrag", StringComparison.OrdinalIgnoreCase)
            || t.Equals("light-rag", StringComparison.OrdinalIgnoreCase))
            return LightRag;

        if (t.Equals(Cognee, StringComparison.OrdinalIgnoreCase)
            || t.Equals("cognee-mcp", StringComparison.OrdinalIgnoreCase))
            return Cognee;

        if (t.Equals(Graphiti, StringComparison.OrdinalIgnoreCase)
            || t.Equals("graphiti-rest", StringComparison.OrdinalIgnoreCase)
            || t.Equals("zep-graphiti", StringComparison.OrdinalIgnoreCase))
            return Graphiti;

        return t;
    }

    /// <summary>True for registered document retrieval adapter ids, usable as the default RAG provider.</summary>
    public static bool IsKnown(string? providerId)
    {
        var id = Normalize(providerId);
        return All.Contains(id, StringComparer.Ordinal);
    }
}
