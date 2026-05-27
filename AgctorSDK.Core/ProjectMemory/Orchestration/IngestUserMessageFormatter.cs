using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AgctorSDK.Core.ProjectMemory.Models;

namespace AgctorSDK.Core.ProjectMemory.Orchestration;

/// <summary>
/// Builds a human-readable playground/API reply after person-extractor ingest —
/// grouped facts by person instead of bare file paths.
/// </summary>
public static class IngestUserMessageFormatter
{
    /// <summary>Format ingest outcome for chat UI (markdown-friendly plain text).</summary>
    public static string Format(
        ProjectMemoryIngestResult ingest,
        string? rawExtractorOutput,
        string? projectRoot = null)
    {
        if (!ingest.ParseSuccess)
        {
            return "I couldn't parse the extracted facts."
                   + (string.IsNullOrWhiteSpace(ingest.Summary) ? "" : " " + ingest.Summary.Trim());
        }

        var sb = new StringBuilder();
        var wrote = ingest.WroteAnyFile;
        var pending = ingest.OutOfSchemaProposals?.Count ?? 0;

        if (MemoryIntentJson.TryParseBatch(rawExtractorOutput ?? "", out var batch, out _, out _)
            && batch?.MemoryIntents is { Count: > 0 } intents)
        {
            foreach (var group in intents
                         .GroupBy(i => i.EntityKey.Trim(), StringComparer.OrdinalIgnoreCase)
                         .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(group.Key))
                    continue;

                var displayName = ResolveDisplayName(group.Key, group);
                sb.Append("Saved for **").Append(displayName).AppendLine("**:");
                sb.AppendLine();
                foreach (var intent in group.OrderBy(IntentSortKey))
                {
                    sb.Append("- **").Append(FormatCategory(intent)).Append(":** ")
                        .AppendLine(FormatValue(intent));
                }

                sb.AppendLine();
            }
        }
        else if (wrote)
        {
            sb.AppendLine("Saved to memory:");
            sb.AppendLine();
        }
        else if (pending == 0)
        {
            return string.IsNullOrWhiteSpace(ingest.Summary)
                ? "No facts were saved."
                : ingest.Summary.Trim();
        }

        if (wrote && ingest.UpdatedFiles.Count > 0)
        {
            sb.AppendLine("**Updated files**");
            foreach (var path in ingest.UpdatedFiles.Take(12))
                sb.AppendLine("- `" + ToDisplayPath(projectRoot, path) + "`");
            if (ingest.UpdatedFiles.Count > 12)
                sb.AppendLine("(+" + (ingest.UpdatedFiles.Count - 12) + " more)");
            sb.AppendLine();
        }

        if (pending > 0)
        {
            sb.AppendLine("**Needs your confirmation** (not saved to structured files yet):");
            foreach (var proposal in ingest.OutOfSchemaProposals!.Take(10))
                sb.AppendLine("- " + proposal.UserPromptLine);
            if (pending > 10)
                sb.AppendLine("(+" + (pending - 10) + " more)");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>True when the user should see extractor ingest summary instead of curator prose.</summary>
    public static bool ShouldPreferIngestSummary(
        ProjectMemoryIngestResult? ingest,
        IEnumerable<string> personasSeen)
    {
        if (ingest is not { ParseSuccess: true })
            return false;
        if (!ingest.WroteAnyFile && (ingest.OutOfSchemaProposals?.Count ?? 0) == 0)
            return false;

        foreach (var persona in personasSeen)
        {
            if (string.IsNullOrWhiteSpace(persona))
                continue;
            if (IsHigherPriorityReplyPersona(persona))
                return false;
        }

        return true;
    }

    private static bool IsHigherPriorityReplyPersona(string personaId)
    {
        return personaId.Equals("person-query", StringComparison.OrdinalIgnoreCase)
               || personaId.Equals("relationship-coach", StringComparison.OrdinalIgnoreCase)
               || personaId.Equals("style-coach", StringComparison.OrdinalIgnoreCase)
               || personaId.Equals("fitness-coach", StringComparison.OrdinalIgnoreCase)
               || personaId.Equals("visual-intake", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveDisplayName(string entityKey, IEnumerable<MemoryIntent> intents)
    {
        var nameIntent = intents.FirstOrDefault(i =>
            i.KnowledgeType.Equals("profile_fact", StringComparison.OrdinalIgnoreCase)
            && i.Attribute != null
            && (i.Attribute.Equals("name", StringComparison.OrdinalIgnoreCase)
                || i.Attribute.Equals("full_name", StringComparison.OrdinalIgnoreCase))
            && !string.IsNullOrWhiteSpace(i.Value));

        if (nameIntent != null)
            return nameIntent.Value.Trim();

        if (entityKey.Length == 0)
            return entityKey;

        return char.ToUpperInvariant(entityKey[0]) + entityKey[1..];
    }

    private static string FormatCategory(MemoryIntent intent)
    {
        if (!string.IsNullOrWhiteSpace(intent.Attribute))
        {
            var attr = intent.Attribute!.Trim().Replace('_', ' ');
            return char.ToUpperInvariant(attr[0]) + attr[1..];
        }

        var kt = (intent.KnowledgeType ?? "fact").Trim().Replace('_', ' ');
        return kt.Length == 0 ? "Fact" : char.ToUpperInvariant(kt[0]) + kt[1..];
    }

    private static string FormatValue(MemoryIntent intent)
    {
        var value = (intent.Value ?? "").Trim();
        if (string.IsNullOrEmpty(value))
            return value;

        if (intent.KnowledgeType.Equals("family_role", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(intent.Attribute))
        {
            return intent.Attribute!.Trim().ToLowerInvariant() + ": " + value;
        }

        return value;
    }

    private static (int, string, string) IntentSortKey(MemoryIntent intent) =>
        (CategoryRank(intent.KnowledgeType), FormatCategory(intent), FormatValue(intent));

    private static int CategoryRank(string? knowledgeType) => (knowledgeType ?? "").ToLowerInvariant() switch
    {
        "profile_fact" => 0,
        "family_role" => 1,
        "education" => 2,
        "occupation" => 3,
        "skill" => 4,
        "preference" => 5,
        "physical_attribute" => 6,
        "event" => 7,
        "observation" => 8,
        _ => 9
    };

    private static string ToDisplayPath(string? projectRoot, string absoluteOrRelative)
    {
        if (string.IsNullOrWhiteSpace(absoluteOrRelative))
            return absoluteOrRelative;

        if (!string.IsNullOrWhiteSpace(projectRoot))
        {
            try
            {
                var root = Path.GetFullPath(projectRoot.Trim());
                var full = Path.GetFullPath(absoluteOrRelative);
                if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    var rel = Path.GetRelativePath(root, full).Replace('\\', '/');
                    return rel;
                }
            }
            catch
            {
                /* fall through */
            }
        }

        return absoluteOrRelative.Replace('\\', '/');
    }
}
