using System;
using System.Collections.Generic;
using System.Linq;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Tools;

namespace AgctorSDK.Core.ProjectMemory.Processing;

/// <summary>
/// Pre-route cleanup for <c>family_role</c> intents: canonical parent edges as <c>child</c> on the parent entity,
/// conservative slug repair when a known display name appears in the user message corpus, symmetric
/// <c>sibling</c>/<c>spouse</c> pairs, and dropping obvious junk (self-loops, unresolved keys).
/// </summary>
public static class FamilyRoleIntentNormalizer
{
    private static readonly HashSet<string> ParentEdgeAttrs = new(StringComparer.OrdinalIgnoreCase)
    {
        "parent", "mother", "father", "mom", "dad", "guardian",
        "stepfather", "stepmother", "stepdad", "stepmom"
    };

    private static readonly HashSet<string> ChildEdgeAttrs = new(StringComparer.OrdinalIgnoreCase)
    {
        "child", "son", "daughter", "kid"
    };

    private static readonly HashSet<string> SiblingEdgeAttrs = new(StringComparer.OrdinalIgnoreCase)
    {
        "sibling", "brother", "sister"
    };

    private static readonly HashSet<string> SpouseEdgeAttrs = new(StringComparer.OrdinalIgnoreCase)
    {
        "spouse", "husband", "wife", "partner"
    };

    /// <summary>
    /// Prefer the latest user line from extractor prompts so substring checks match what the human typed.
    /// </summary>
    public static string ExtractUserMessageCorpus(string rawExtract)
    {
        if (string.IsNullOrEmpty(rawExtract))
            return "";
        const string marker = "Latest user message:";
        var idx = rawExtract.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0)
            return rawExtract;
        return rawExtract[(idx + marker.Length)..].Trim();
    }

    /// <summary>
    /// Rewrites <paramref name="intents"/> in place: non–<c>family_role</c> rows are preserved;
    /// family rows are normalized, resolved, de-duplicated, and augmented with inverse sibling/spouse edges.
    /// </summary>
    public static void Apply(
        List<MemoryIntent> intents,
        IReadOnlyList<EntityRecord> discovered,
        string rawExtractForCorpus,
        IList<string> notes)
    {
        if (intents.Count == 0)
            return;

        var corpus = ExtractUserMessageCorpus(rawExtractForCorpus);
        var lookup = BuildEntityLookup(discovered);

        var kept = new List<MemoryIntent>();
        foreach (var intent in intents)
        {
            if (!string.Equals(intent.KnowledgeType, "family_role", StringComparison.OrdinalIgnoreCase))
            {
                kept.Add(Clone(intent));
                continue;
            }

            var working = Clone(intent);

            // "X parent Y" → Y has child X (canonical child edge on parent).
            if (ParentEdgeAttrs.Contains(working.Attribute ?? ""))
            {
                var childToken = working.EntityKey?.Trim() ?? "";
                var parentToken = working.Value?.Trim() ?? "";
                working.EntityKey = parentToken;
                working.Value = childToken;
                working.Attribute = "child";
                notes.Add(
                    $"family_role: normalized parent-type edge to canonical child (parent='{parentToken}', child='{childToken}').");
            }
            else if (ChildEdgeAttrs.Contains(working.Attribute ?? ""))
            {
                working.Attribute = "child";
            }
            else if (SiblingEdgeAttrs.Contains(working.Attribute ?? ""))
            {
                working.Attribute = "sibling";
            }
            else if (SpouseEdgeAttrs.Contains(working.Attribute ?? ""))
            {
                working.Attribute = "spouse";
            }

            if (string.IsNullOrWhiteSpace(working.Value))
            {
                notes.Add($"family_role: dropped intent — empty value (entityKey='{working.EntityKey}', attr='{working.Attribute}').");
                continue;
            }

            var ek = TryResolveEntityToken(working.EntityKey, corpus, discovered, lookup, out var ekNote);
            if (ekNote != null)
                notes.Add(ekNote);
            if (string.IsNullOrEmpty(ek))
            {
                notes.Add($"family_role: dropped intent — unresolved entityKey '{working.EntityKey}'.");
                continue;
            }

            var vk = TryResolveEntityToken(working.Value, corpus, discovered, lookup, out var vkNote);
            if (vkNote != null)
                notes.Add(vkNote);
            if (string.IsNullOrEmpty(vk))
            {
                notes.Add($"family_role: dropped intent — unresolved value '{working.Value}'.");
                continue;
            }

            working.EntityKey = ek;
            working.Value = vk;

            if (string.Equals(working.EntityKey, working.Value, StringComparison.OrdinalIgnoreCase))
            {
                notes.Add($"family_role: dropped self-loop ({working.EntityKey}, {working.Attribute}).");
                continue;
            }

            kept.Add(working);
        }

        AddSymmetricFamilyEdges(kept, notes);
        AddParentChildInverseEdges(kept, notes);
        DedupeFamilyIntents(kept);

        intents.Clear();
        intents.AddRange(kept);
    }

    private static void DedupeFamilyIntents(List<MemoryIntent> intents)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var i = 0;
        while (i < intents.Count)
        {
            var m = intents[i];
            if (!string.Equals(m.KnowledgeType, "family_role", StringComparison.OrdinalIgnoreCase))
            {
                i++;
                continue;
            }

            var key = m.KnowledgeType + "|" + m.EntityKey + "|" + (m.Attribute ?? "") + "|" + m.Value;
            if (!seen.Add(key))
            {
                intents.RemoveAt(i);
                continue;
            }

            i++;
        }
    }

    /// <summary>
    /// Parent/child edges are not symmetric (same attribute on both sides), so add the directional
    /// inverse so both entities' <c>relationships.md</c> end up with a matching row. For a canonical
    /// <c>(parent, child, child)</c> edge this emits <c>(child, parent, parent)</c>.
    /// </summary>
    private static void AddParentChildInverseEdges(List<MemoryIntent> intents, IList<string> notes)
    {
        var family = intents.Where(m => string.Equals(m.KnowledgeType, "family_role", StringComparison.OrdinalIgnoreCase)).ToList();
        var toAdd = new List<MemoryIntent>();
        foreach (var m in family)
        {
            var attr = m.Attribute ?? "";
            string? inverseAttr = null;
            if (string.Equals(attr, "child", StringComparison.OrdinalIgnoreCase)) inverseAttr = "parent";
            else if (string.Equals(attr, "parent", StringComparison.OrdinalIgnoreCase)) inverseAttr = "child";
            if (inverseAttr == null) continue;

            var hasInverse = family.Any(x =>
                string.Equals(x.Attribute, inverseAttr, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.EntityKey, m.Value, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Value, m.EntityKey, StringComparison.OrdinalIgnoreCase));
            if (hasInverse) continue;

            toAdd.Add(new MemoryIntent
            {
                EntityKey = m.Value,
                KnowledgeType = "family_role",
                Attribute = inverseAttr,
                Value = m.EntityKey,
                Confidence = m.Confidence
            });
            notes.Add($"family_role: added inverse {inverseAttr} edge {m.Value} → {m.EntityKey}.");
        }

        intents.AddRange(toAdd);
    }

    private static void AddSymmetricFamilyEdges(List<MemoryIntent> intents, IList<string> notes)
    {
        var family = intents.Where(m => string.Equals(m.KnowledgeType, "family_role", StringComparison.OrdinalIgnoreCase)).ToList();
        var toAdd = new List<MemoryIntent>();
        foreach (var m in family)
        {
            var attr = m.Attribute ?? "";
            if (!string.Equals(attr, "sibling", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(attr, "spouse", StringComparison.OrdinalIgnoreCase))
                continue;

            var hasInverse = family.Any(x =>
                string.Equals(x.Attribute, attr, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.EntityKey, m.Value, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Value, m.EntityKey, StringComparison.OrdinalIgnoreCase));
            if (hasInverse)
                continue;

            toAdd.Add(new MemoryIntent
            {
                EntityKey = m.Value,
                KnowledgeType = "family_role",
                Attribute = m.Attribute,
                Value = m.EntityKey,
                Confidence = m.Confidence
            });
            notes.Add($"family_role: added symmetric {attr} edge {m.Value} → {m.EntityKey}.");
        }

        intents.AddRange(toAdd);
    }

    private static string? TryResolveEntityToken(
        string? rawToken,
        string corpus,
        IReadOnlyList<EntityRecord> discovered,
        Dictionary<string, EntityRecord> lookup,
        out string? note)
    {
        note = null;
        if (string.IsNullOrWhiteSpace(rawToken))
            return null;

        var trimmed = rawToken.Trim();
        var leaf = EntityFolderBootstrapper.SlugFolderSegment(trimmed);
        if (string.IsNullOrEmpty(leaf))
            return null;

        // Exact / alias map (alphanumeric-normalized keys).
        var norm = NormalizeKey(trimmed);
        if (lookup.TryGetValue(norm, out var direct))
            return direct.EntityKey;

        // Path-style keys: try last segment against lookup.
        var leafNorm = NormalizeKey(leaf);
        if (lookup.TryGetValue(leafNorm, out var byLeaf))
            return byLeaf.EntityKey;

        // Conservative fuzzy: only when a corroborating name appears in the same user corpus.
        var fuzzy = FuzzyMatchSingleEntity(leafNorm, corpus, discovered);
        if (fuzzy != null)
        {
            note = $"family_role: fuzzy-matched token '{trimmed}' → entity '{fuzzy.EntityKey}'.";
            return fuzzy.EntityKey;
        }

        // Brand-new folder slug (e.g. first mention of "Melody") — keep slug so ingest can bootstrap.
        // If exactly one on-disk entity is within edit distance 1 of this slug and the user text does not
        // corroborate that entity's display name, treat as an unresolved typo (e.g. rafa vs raha).
        var near = NearNeighborEntities(leafNorm, discovered);
        if (near.Count >= 2)
        {
            note = $"family_role: ambiguous near-match for '{trimmed}' — not resolved.";
            return null;
        }

        if (near.Count == 1 && !CorpusNamesCorroborateEntity(corpus, near[0]))
            return null;

        return string.IsNullOrEmpty(leaf) ? null : leaf;
    }

    /// <summary>
    /// Pick at most one entity where Levenshtein(slug, entityFolderSlug) ≤ 1 and corpus contains display or alias verbatim.
    /// </summary>
    private static List<EntityRecord> NearNeighborEntities(string tokenNorm, IReadOnlyList<EntityRecord> discovered)
    {
        var list = new List<EntityRecord>();
        if (string.IsNullOrEmpty(tokenNorm))
            return list;
        foreach (var e in discovered)
        {
            var slugNorm = NormalizeKey(e.EntityKey);
            if (slugNorm.Length == 0)
                continue;
            if (Levenshtein(tokenNorm, slugNorm) <= 1)
                list.Add(e);
        }

        return list;
    }

    private static EntityRecord? FuzzyMatchSingleEntity(string tokenNorm, string corpus, IReadOnlyList<EntityRecord> discovered)
    {
        if (string.IsNullOrEmpty(tokenNorm) || discovered.Count == 0)
            return null;

        EntityRecord? sole = null;
        foreach (var e in discovered)
        {
            var slugNorm = NormalizeKey(e.EntityKey);
            if (slugNorm.Length == 0)
                continue;
            if (Levenshtein(tokenNorm, slugNorm) > 1)
                continue;

            if (!CorpusNamesCorroborateEntity(corpus, e))
                continue;

            if (sole != null && !string.Equals(sole.EntityKey, e.EntityKey, StringComparison.OrdinalIgnoreCase))
                return null; // ambiguous

            sole = e;
        }

        return sole;
    }

    private static bool CorpusNamesCorroborateEntity(string corpus, EntityRecord e)
    {
        if (CorpusContainsInsensitive(corpus, e.Metadata.DisplayName))
            return true;
        if (e.Metadata.Aliases == null)
            return false;
        return e.Metadata.Aliases.Any(a => CorpusContainsInsensitive(corpus, a));
    }

    private static bool CorpusContainsInsensitive(string corpus, string? fragment)
    {
        if (string.IsNullOrWhiteSpace(fragment))
            return false;
        return corpus.IndexOf(fragment.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static int Levenshtein(string a, string b)
    {
        if (a == b)
            return 0;
        if (a.Length == 0)
            return b.Length;
        if (b.Length == 0)
            return a.Length;
        var n = a.Length;
        var m = b.Length;
        var prev = new int[m + 1];
        var curr = new int[m + 1];
        for (var j = 0; j <= m; j++)
            prev[j] = j;
        for (var i = 1; i <= n; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= m; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }

            (prev, curr) = (curr, prev);
        }

        return prev[m];
    }

    private static Dictionary<string, EntityRecord> BuildEntityLookup(IReadOnlyList<EntityRecord> discovered)
    {
        var map = new Dictionary<string, EntityRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in discovered)
        {
            AddLookup(map, e.EntityKey, e);
            AddLookup(map, e.Metadata.DisplayName, e);
            if (e.Metadata.Aliases != null)
            {
                foreach (var a in e.Metadata.Aliases)
                    AddLookup(map, a, e);
            }
        }

        return map;
    }

    private static void AddLookup(Dictionary<string, EntityRecord> map, string? raw, EntityRecord rec)
    {
        var k = NormalizeKey(raw);
        if (string.IsNullOrEmpty(k))
            return;
        if (!map.ContainsKey(k))
            map[k] = rec;
    }

    private static string NormalizeKey(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";
        var s = raw.Trim().ToLowerInvariant();
        var span = s.AsSpan();
        Span<char> buf = stackalloc char[span.Length];
        var w = 0;
        foreach (var c in span)
        {
            if (char.IsLetterOrDigit(c))
                buf[w++] = c;
        }

        return w == 0 ? "" : new string(buf[..w]);
    }

    private static MemoryIntent Clone(MemoryIntent m) => new()
    {
        EntityKey = m.EntityKey,
        KnowledgeType = m.KnowledgeType,
        Attribute = m.Attribute,
        Value = m.Value,
        Confidence = m.Confidence
    };
}
