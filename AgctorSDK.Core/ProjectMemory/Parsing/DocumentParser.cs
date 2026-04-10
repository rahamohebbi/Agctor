using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace AgctorSDK.Core.ProjectMemory.Parsing;

public sealed class DocumentParser : IDocumentParser
{
    private static readonly Regex Heading = new(@"^##\s+(.+)\s*$", RegexOptions.Multiline | RegexOptions.Compiled);

    public ParsedMarkdownDocument Parse(string markdownContent)
    {
        var raw = markdownContent ?? "";
        var matches = Heading.Matches(raw);
        var sections = new List<MarkdownSection>();
        if (matches.Count == 0)
            return new ParsedMarkdownDocument { Raw = raw, Sections = sections };

        for (var i = 0; i < matches.Count; i++)
        {
            var m = matches[i];
            var title = m.Groups[1].Value.Trim();
            var start = m.Index + m.Length;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : raw.Length;
            var body = raw.Substring(start, end - start).TrimEnd();
            var lineIdx = raw[..m.Index].Split('\n').Length - 1;
            sections.Add(new MarkdownSection { Title = title, Body = body, StartLineIndex = lineIdx });
        }

        return new ParsedMarkdownDocument { Raw = raw, Sections = sections };
    }

    /// <summary>Rebuild markdown from sections preserving heading order.</summary>
    public static string Compose(string titleLine, IReadOnlyList<(string SectionTitle, string Body)> sections)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(titleLine))
        {
            sb.AppendLine(titleLine.TrimEnd());
            sb.AppendLine();
        }

        for (var i = 0; i < sections.Count; i++)
        {
            var (t, b) = sections[i];
            sb.AppendLine($"## {t}");
            sb.AppendLine();
            sb.AppendLine(b.TrimEnd());
            if (i < sections.Count - 1)
                sb.AppendLine();
        }

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }
}
