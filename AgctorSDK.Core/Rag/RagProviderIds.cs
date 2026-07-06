namespace AgctorSDK.Core.Rag;

/// <summary>Canonical provider ids shared by catalog, factory, and configuration.</summary>
public static class RagProviderIds
{
    public const string None = "None";
    public const string LightRag = "LightRAG";
    public const string Cognee = "Cognee";

    /// <summary>Factory/catalog ids in display order.</summary>
    public static readonly IReadOnlyList<string> All = new[] { None, LightRag, Cognee };

    /// <summary>Maps aliases to canonical ids; returns None when blank.</summary>
    public static string Normalize(string? providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
            return None;

        var t = providerId.Trim();
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

        return t;
    }

    /// <summary>True for registered v1 catalog ids.</summary>
    public static bool IsKnown(string? providerId)
    {
        var id = Normalize(providerId);
        return All.Contains(id, StringComparer.Ordinal);
    }
}
