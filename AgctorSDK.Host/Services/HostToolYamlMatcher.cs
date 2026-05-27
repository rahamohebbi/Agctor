using System.Text;
using AgctorSDK.Extensions.Services;

namespace AgctorSDK.Host.Services;

/// <summary>Maps YAML <c>tools.allow</c> tokens to registered host tool catalog entries.</summary>
public static class HostToolYamlMatcher
{
    public static bool TokenMatchesHostTool(string token, AgctorToolCatalog.ToolCatalogEntry entry)
    {
        var t = token.Trim();
        if (string.Equals(t, entry.ClrTypeName, StringComparison.OrdinalIgnoreCase))
            return true;
        if (entry.ExposeOnHttpApi && string.Equals(t, entry.PrimaryId, StringComparison.OrdinalIgnoreCase))
            return true;
        return AlphanumericKey(t) == AlphanumericKey(StripToolSuffix(entry.ClrTypeName));
    }

    public static bool IsKnownSemanticToken(string token)
    {
        var t = token.Trim().ToLowerInvariant();
        return t is "read_document" or "write_document" or "search_entities" or "load_schema" or "memory_intents_only";
    }

    public static string? FindMatchingToken(IEnumerable<string> allowTokens, AgctorToolCatalog.ToolCatalogEntry entry)
    {
        foreach (var raw in allowTokens)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            if (TokenMatchesHostTool(raw, entry))
                return raw.Trim();
        }

        return null;
    }

    private static string StripToolSuffix(string clrName)
    {
        if (clrName.EndsWith("Tool", StringComparison.OrdinalIgnoreCase) && clrName.Length > 4)
            return clrName[..^4];
        return clrName;
    }

    private static string AlphanumericKey(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }
}
