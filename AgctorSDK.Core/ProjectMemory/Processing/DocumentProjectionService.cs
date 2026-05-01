using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Parsing;

namespace AgctorSDK.Core.ProjectMemory.Processing;

/// <summary>
/// Deterministic markdown updates: replace_section, merge_list, append_chronological (PRD §20).
/// </summary>
public sealed class DocumentProjectionService : IDocumentProjectionService
{
    private readonly IDocumentParser _parser;

    public DocumentProjectionService(IDocumentParser parser)
    {
        _parser = parser;
    }

    public async Task<ProjectionResult> ApplyAsync(EntityRecord entity, IReadOnlyList<RoutedMemoryIntent> intents, CancellationToken cancellationToken = default)
    {
        var result = new ProjectionResult();
        var byFile = intents.GroupBy(i => i.FileName, StringComparer.OrdinalIgnoreCase);

        foreach (var g in byFile)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(entity.RootPath, g.Key);
            if (!File.Exists(path))
            {
                result.Issues.Add(new ValidationIssue
                {
                    Code = "missing_file",
                    Message = $"Document not found: {path}",
                    Path = path,
                    IsError = true
                });
                continue;
            }

            var raw = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            var doc = _parser.Parse(raw);
            var titleLine = ExtractTitleLine(raw);

            // Work on a mutable list of (title, body) aligned with parser
            var map = doc.Sections.ToDictionary(s => NormalizeTitle(s.Title), s => s.Body, StringComparer.OrdinalIgnoreCase);
            var order = doc.Sections.Select(s => NormalizeTitle(s.Title)).ToList();

            foreach (var intent in g)
            {
                var key = NormalizeTitle(intent.SectionTitle);
                if (!map.TryGetValue(key, out var body))
                {
                    result.Issues.Add(new ValidationIssue
                    {
                        Code = "section",
                        Message = $"Section '{intent.SectionTitle}' not found in {g.Key}",
                        Path = path,
                        IsError = true
                    });
                    continue;
                }

                map[key] = intent.UpdateMode switch
                {
                    "merge_list" => MergeList(body, FormatMergeEntry(intent.Original)),
                    "append_chronological" => AppendChronological(body, intent.Original.Value),
                    _ => ReplaceSectionBody(body, intent.Original)
                };
            }

            var rebuilt = BuildFromOrder(order, map, titleLine, doc);
            await File.WriteAllTextAsync(path, rebuilt, cancellationToken).ConfigureAwait(false);
            result.UpdatedFiles.Add(path);
        }

        return result;
    }

    private static string NormalizeTitle(string t) => t.Trim();

    private static string? ExtractTitleLine(string raw)
    {
        var first = raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)[0];
        if (first.StartsWith("# "))
            return first;
        return null;
    }

    private static string ReplaceSectionBody(string body, MemoryIntent intent)
    {
        // Keep multiple key-value facts in the same section (e.g. Name + Age in Basic Info).
        var label = LabelFor(intent).Trim();
        var value = (intent.Value ?? "").Trim();
        if (string.IsNullOrEmpty(label))
            return body;

        var rows = ParseKeyValueRows(body, out var useBullets);
        var idx = rows.FindIndex(r => r.Label.Equals(label, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
            rows[idx] = (rows[idx].Label, value);
        else
            rows.Add((label, value));

        var normalized = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Label))
            .Select(r => $"{r.Label}: {r.Value}".Trim())
            .Where(s => s.Length > 0)
            .ToList();

        if (normalized.Count == 0)
            return body;
        if (useBullets)
            return string.Join(Environment.NewLine, normalized.Select(n => "- " + n));
        return string.Join(Environment.NewLine, normalized);
    }

    private static List<(string Label, string Value)> ParseKeyValueRows(string body, out bool useBullets)
    {
        var rows = new List<(string Label, string Value)>();
        var lines = (body ?? "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        useBullets = lines.Any(l => l.StartsWith("- ", StringComparison.Ordinal));
        foreach (var line in lines)
        {
            var t = line.StartsWith("- ", StringComparison.Ordinal) ? line[2..].Trim() : line;
            var colon = t.IndexOf(':');
            if (colon <= 0)
                continue;
            var label = t[..colon].Trim();
            var value = t[(colon + 1)..].Trim();
            if (label.Length == 0)
                continue;
            rows.Add((label, value));
        }

        return rows;
    }

    private static string LabelFor(MemoryIntent intent)
    {
        if (!string.IsNullOrEmpty(intent.Attribute))
            return char.ToUpperInvariant(intent.Attribute[0]) + intent.Attribute[1..].Replace("_", " ");
        return char.ToUpperInvariant(intent.KnowledgeType[0]) + intent.KnowledgeType[1..].Replace("_", " ");
    }

    private static string MergeList(string body, string value)
    {
        var line = $"- {value.Trim()}";
        var lines = body.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        if (lines.Any(l => l.Trim().Equals(line, StringComparison.OrdinalIgnoreCase)))
            return body;
        var sb = new StringBuilder(body.TrimEnd());
        if (sb.Length > 0)
            sb.AppendLine();
        sb.AppendLine(line);
        return sb.ToString();
    }

    /// <summary>
    /// Formats the value a merge_list section should receive. Family edges carry a meaningful
    /// <c>Attribute</c> (e.g. <c>child</c>, <c>parent</c>, <c>sibling</c>); without it the
    /// relationship file would just list bare entity keys and lose the relation type. Other
    /// knowledge types fall back to the raw value so existing skills/preferences merging is intact.
    /// </summary>
    private static string FormatMergeEntry(MemoryIntent intent)
    {
        var value = (intent.Value ?? "").Trim();
        if (string.IsNullOrEmpty(value)) return value;

        if (string.Equals(intent.KnowledgeType, "family_role", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(intent.Attribute))
        {
            var attr = intent.Attribute!.Trim().ToLowerInvariant();
            return $"{attr}: {value}";
        }

        return value;
    }

    private static string AppendChronological(string body, string value)
    {
        var ts = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
        var line = $"- [{ts}] {value.Trim()}";
        var sb = new StringBuilder(body.TrimEnd());
        if (sb.Length > 0)
            sb.AppendLine();
        sb.AppendLine(line);
        return sb.ToString();
    }

    private static string BuildFromOrder(IReadOnlyList<string> order, IReadOnlyDictionary<string, string> map, string? titleLine, ParsedMarkdownDocument doc)
    {
        var pairs = order.Select(t => (SectionTitle: DenormalizeTitle(t, doc), Body: map[t])).ToList();
        return DocumentParser.Compose(titleLine ?? "", pairs);
    }

    private static string DenormalizeTitle(string normalized, ParsedMarkdownDocument doc)
    {
        var orig = doc.Sections.FirstOrDefault(s => string.Equals(NormalizeTitle(s.Title), normalized, StringComparison.OrdinalIgnoreCase));
        return orig?.Title ?? normalized;
    }
}
