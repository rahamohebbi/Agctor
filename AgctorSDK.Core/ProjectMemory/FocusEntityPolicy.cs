using System;
using System.Collections.Generic;
using System.Linq;
using AgctorSDK.Core.ProjectMemory.Coref;

namespace AgctorSDK.Core.ProjectMemory;

/// <summary>
/// Guards against generic LLM slugs (user, unknown, he) being treated as real people folders.
/// </summary>
public static class FocusEntityPolicy
{
    private static readonly HashSet<string> PlaceholderSlugs = new(StringComparer.OrdinalIgnoreCase)
    {
        "user", "unknown", "he", "she", "they", "person", "person1", "someone", "subject", "me", "self"
    };

    /// <summary>True when the slug is a generic placeholder, not a real person folder.</summary>
    public static bool IsPlaceholderSlug(string? slug) =>
        !string.IsNullOrWhiteSpace(slug) && PlaceholderSlugs.Contains(slug.Trim());

    /// <summary>Returns null for placeholder slugs; otherwise the trimmed slug.</summary>
    public static string? NormalizeSlugOrNull(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return null;
        var t = slug.Trim();
        return IsPlaceholderSlug(t) ? null : t;
    }

    /// <summary>
    /// When a chat project has no explicit focus, match its display name to a scenario entity
    /// (e.g. project "Raha" → entityKey <c>raha</c>).
    /// </summary>
    public static (string EntityKey, string DisplayName)? TryInferFromProjectName(
        string? projectName,
        IReadOnlyList<(string EntityKey, string DisplayName)> entities)
    {
        var name = projectName?.Trim();
        if (string.IsNullOrWhiteSpace(name) || entities == null || entities.Count == 0)
            return null;

        foreach (var e in entities)
        {
            if (IsPlaceholderSlug(e.EntityKey))
                continue;

            if (string.Equals(e.EntityKey, name, StringComparison.OrdinalIgnoreCase))
                return (e.EntityKey, e.DisplayName);

            if (string.Equals(e.DisplayName, name, StringComparison.OrdinalIgnoreCase))
                return (e.EntityKey, e.DisplayName);

            var firstToken = e.DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (firstToken != null && string.Equals(firstToken, name, StringComparison.OrdinalIgnoreCase))
                return (e.EntityKey, e.DisplayName);
        }

        return null;
    }

    /// <summary>Prefer project focus over placeholder active-subject hints from coref/extract.</summary>
    public static string? CoalesceActiveSubject(string? candidate, string? projectFocusKey)
    {
        var normalized = NormalizeSlugOrNull(candidate);
        if (!string.IsNullOrWhiteSpace(normalized))
            return normalized;

        return NormalizeSlugOrNull(projectFocusKey);
    }

    /// <summary>
    /// When a message names multiple known people, pick the one mentioned earliest (e.g. "Ryan is Raha's son" → ryan).
    /// </summary>
    public static (string EntityKey, string DisplayName)? TryMatchPrimaryEntityInMessage(
        string? message,
        IReadOnlyList<(string EntityKey, string DisplayName)> entities)
    {
        if (string.IsNullOrWhiteSpace(message) || entities == null || entities.Count == 0)
            return null;

        string? bestKey = null;
        string? bestDisplay = null;
        var bestIndex = int.MaxValue;

        foreach (var e in entities)
        {
            if (IsPlaceholderSlug(e.EntityKey))
                continue;

            var idx = IndexOfWord(message, e.EntityKey);
            if (idx < 0 && !string.IsNullOrWhiteSpace(e.DisplayName))
                idx = IndexOfWord(message, e.DisplayName);

            if (idx < 0 || idx >= bestIndex)
                continue;

            bestIndex = idx;
            bestKey = e.EntityKey;
            bestDisplay = e.DisplayName;
        }

        return bestKey == null ? null : (bestKey, bestDisplay ?? bestKey);
    }

    /// <summary>Same as tuple overload but for <see cref="KnownEntity"/> lists used by coref.</summary>
    public static string? TryMatchPrimaryEntityKeyInMessage(string? message, IEnumerable<KnownEntity>? entities)
    {
        if (entities == null)
            return null;

        var list = new List<(string EntityKey, string DisplayName)>();
        foreach (var e in entities)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.EntityKey))
                continue;
            list.Add((e.EntityKey, e.DisplayName ?? e.EntityKey));
        }

        return TryMatchPrimaryEntityInMessage(message, list)?.EntityKey;
    }

    private static int IndexOfWord(string haystack, string? needle)
    {
        if (string.IsNullOrWhiteSpace(needle))
            return -1;

        var trimmed = needle.Trim();
        var pattern = $@"\b{System.Text.RegularExpressions.Regex.Escape(trimmed)}\b";
        var m = System.Text.RegularExpressions.Regex.Match(
            haystack,
            pattern,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
                | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return m.Success ? m.Index : -1;
    }
}
