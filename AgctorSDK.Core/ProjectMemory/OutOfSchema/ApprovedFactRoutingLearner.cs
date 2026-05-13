using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Tools;
using AgctorSDK.Core.ProjectMemory.Yaml;

namespace AgctorSDK.Core.ProjectMemory.OutOfSchema;

/// <summary>
/// After the user approves an out-of-schema (route_miss) fact, append routing rules so the same
/// extractor shape routes on the next turn without another confirmation prompt.
/// </summary>
public static class ApprovedFactRoutingLearner
{
    private static readonly ConcurrentDictionary<string, object> FileLocks = new();

    private static object LockFor(string path) => FileLocks.GetOrAdd(path, _ => new object());

    /// <summary>Filters <paramref name="approvals"/> to rows actually appended, then updates routing YAML.</summary>
    public static IReadOnlyList<string> TryLearnAfterPersist(
        LoadedProjectContext ctx,
        GenericInboxPersistResult persistResult,
        IReadOnlyList<ApprovedGenericFact> approvals)
    {
        if (persistResult.Appended <= 0 || persistResult.AppendedProposalIds.Count == 0 || approvals.Count == 0)
            return Array.Empty<string>();

        var idSet = persistResult.AppendedProposalIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var facts = approvals.Where(a => idSet.Contains(a.ProposalId)).ToList();
        return TryAppendRules(ctx, facts);
    }

    /// <summary>Heuristic bucket for <c>knowledgeType</c> + <c>attribute</c> → profile document section.</summary>
    public static (string DocumentId, string SectionTitle) GuessTarget(string knowledgeType, string? attribute)
    {
        var bag = $"{knowledgeType} {attribute ?? ""}".ToLowerInvariant();
        // Pad so short tokens like "live" do not match inside unrelated words (e.g. "deliver").
        var padded = $" {bag} ";

        // Location / whereabouts — keep with other stable identity context on the profile.
        if (ContainsAny(bag, "location", "city", "address", "residence", "hometown", "country", "region", "state",
                "province", "zip", "postal", "neighborhood", "based", "lives", "moved")
            || WordBoundaryContains(padded, "live")
            || WordBoundaryContains(padded, "living"))
            return ("profile", "Basic Info");

        // Work-ish details (including profile_fact/occupation when the model uses attribute instead of knowledgeType).
        if (ContainsAny(bag, "occupation", "employer", "workplace", "office", "job title", "job_title", "works at"))
            return ("profile", "Basic Info");

        // Catch-all for pets, vehicles, hobbies without a dedicated doc — profile Notes matches curator copy.
        return ("profile", "Notes");
    }

    /// <summary>
    /// Merges learned rules into <c>routing-rules.yaml</c>. Skips when disabled, paths missing, or rule already present.
    /// </summary>
    /// <returns>Human-readable lines for UI / pipeline summary.</returns>
    public static IReadOnlyList<string> TryAppendRules(
        LoadedProjectContext ctx,
        IReadOnlyList<ApprovedGenericFact> newlyAppendedFacts)
    {
        var lines = new List<string>();
        if (newlyAppendedFacts.Count == 0)
            return lines;

        if (ctx.ResolvedSchemaPaths == null || string.IsNullOrWhiteSpace(ctx.ResolvedSchemaPaths.RoutingRulesYaml))
            return lines;

        if (ctx.Runtime.OutOfSchema?.LearnRoutingOnApprove == false)
            return lines;

        var routingPath = Path.GetFullPath(ctx.ResolvedSchemaPaths.RoutingRulesYaml.Trim());
        if (!ProjectMemoryAccessGuard.IsUnderProjectAgctor(ctx.ProjectRoot, routingPath))
        {
            lines.Add("routing learn skipped: routing path outside project .agctor.");
            return lines;
        }

        var docTypes = ctx.TypeSchema.DocumentTypes;

        lock (LockFor(routingPath))
        {
            RoutingRulesSchema schema;
            try
            {
                var text = File.Exists(routingPath) ? File.ReadAllText(routingPath) : "";
                schema = string.IsNullOrWhiteSpace(text)
                    ? new RoutingRulesSchema()
                    : ProjectYamlSerializer.Deserialize<RoutingRulesSchema>(text);
            }
            catch (Exception ex)
            {
                lines.Add("routing learn skipped: could not read routing-rules.yaml — " + ex.Message);
                return lines;
            }

            schema.RoutingRules ??= new List<RoutingRule>();
            var added = 0;
            var learnedDescriptions = new List<string>();
            foreach (var fact in newlyAppendedFacts)
            {
                if (string.IsNullOrWhiteSpace(fact.KnowledgeType))
                    continue;

                var attrNorm = string.IsNullOrWhiteSpace(fact.Attribute) ? null : fact.Attribute.Trim();
                if (RuleExists(schema.RoutingRules, fact.KnowledgeType, attrNorm))
                    continue;

                var (docId, section) = GuessTarget(fact.KnowledgeType.Trim(), attrNorm);
                var (safeDoc, safeSection) = NormalizeAgainstDocumentTypes(docTypes, docId, section);

                schema.RoutingRules.Add(new RoutingRule
                {
                    When = new RoutingWhen
                    {
                        KnowledgeType = fact.KnowledgeType.Trim(),
                        Attribute = attrNorm
                    },
                    Target = new RoutingTarget { Document = safeDoc, Section = safeSection }
                });
                added++;
                var attrDisp = attrNorm == null ? "" : "/" + attrNorm;
                learnedDescriptions.Add($"Learned routing: {fact.KnowledgeType}{attrDisp} → {safeDoc}/{safeSection}.");
            }

            if (added == 0)
                return lines;

            try
            {
                File.WriteAllText(routingPath, ProjectYamlSerializer.Serialize(schema));
            }
            catch (Exception ex)
            {
                lines.Add("routing learn failed on write — " + ex.Message);
                return lines;
            }

            // Use a path relative to the project so parity tests and logs stay stable across machines/temp dirs.
            var displayPath = TryRelativeUnderProject(ctx.ProjectRoot, routingPath);
            lines.Add($"Updated routing rules ({added} new rule(s)) in {displayPath}.");
            lines.Add(ProjectMemoryDashboardPaths.WorkspaceDeepLinkLine(displayPath));
            lines.AddRange(learnedDescriptions);
        }

        return lines;
    }

    private static string TryRelativeUnderProject(string projectRoot, string absolutePath)
    {
        try
        {
            var pr = Path.GetFullPath(projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var p = Path.GetFullPath(absolutePath);
            if (p.StartsWith(pr + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || string.Equals(p, pr, StringComparison.OrdinalIgnoreCase))
                return Path.GetRelativePath(pr, p).Replace('\\', '/');
        }
        catch
        {
        }

        return Path.GetFileName(absolutePath) ?? absolutePath;
    }

    private static bool WordBoundaryContains(string spacePaddedHaystack, string word)
    {
        return spacePaddedHaystack.Contains($" {word} ", StringComparison.Ordinal);
    }

    private static bool ContainsAny(string haystack, params string[] needles)
    {
        foreach (var n in needles)
        {
            if (haystack.Contains(n, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool RuleExists(IReadOnlyList<RoutingRule> rules, string knowledgeType, string? attribute)
    {
        foreach (var r in rules)
        {
            if (r?.When == null) continue;
            if (!string.Equals(r.When.KnowledgeType, knowledgeType, StringComparison.OrdinalIgnoreCase))
                continue;

            var ruleAttr = string.IsNullOrWhiteSpace(r.When.Attribute) ? null : r.When.Attribute.Trim();
            // Wildcard rule (no attribute on disk) matches every attribute for that knowledgeType.
            if (ruleAttr == null)
                return true;
            if (string.Equals(ruleAttr, attribute ?? "", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static (string DocId, string Section) NormalizeAgainstDocumentTypes(
        DocumentTypesSchema docTypes,
        string documentId,
        string sectionTitle)
    {
        var types = docTypes.DocumentTypes ?? new List<DocumentTypeDef>();
        var doc = types.FirstOrDefault(d => string.Equals(d.Id, documentId, StringComparison.OrdinalIgnoreCase))
                  ?? types.FirstOrDefault(d => string.Equals(d.Id, "profile", StringComparison.OrdinalIgnoreCase))
                  ?? types.FirstOrDefault();

        if (doc == null)
            return ("profile", sectionTitle);

        var sections = doc.Sections ?? new List<string>();
        if (sections.Any(s => string.Equals(s, sectionTitle, StringComparison.Ordinal)))
            return (doc.Id, sectionTitle);

        var fallback = sections.FirstOrDefault(s => string.Equals(s, "Notes", StringComparison.OrdinalIgnoreCase))
                       ?? sections.FirstOrDefault();
        return (doc.Id, fallback ?? sectionTitle);
    }
}
