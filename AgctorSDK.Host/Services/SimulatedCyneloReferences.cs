// Compatibility snapshot of Cynelo.Application.Generation, verified 2026-11-04.
// Used only by the explicit H3 simulator; keep parity fixtures in sync when Cynelo changes.

namespace AgctorSDK.Host.Services;

/// <summary>
/// MiniMax H3 Reference-to-Video addresses stills as &lt;Picture N&gt;.
/// Map the user's names (baker, bakery) onto those tags.
/// </summary>
internal static class SimulatedCyneloReferences
{
    public const int MaxImages = 9;

    public static string Bind(string prompt, IReadOnlyList<(string Label, string Tag)> subjects)
    {
        prompt = prompt.Trim();
        // Older prompts used &lt;image N&gt;; the tokenizer labels vision blocks &lt;Picture N&gt;.
        prompt = OutsideDialogue(prompt, part => System.Text.RegularExpressions.Regex.Replace(
            part,
            @"<image\s+(\d+)>",
            "<Picture $1>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        if (subjects.Count == 0)
        {
            return prompt;
        }

        var groups = subjects.Where(s => !string.IsNullOrWhiteSpace(s.Label) && s.Label != s.Tag)
            .GroupBy(s => s.Label.Trim(), StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var group in groups.OrderByDescending(g => g.Key.Length))
        {
            var tags = group.Select(s => s.Tag).ToArray();
            var replacement = tags.Length == 1 ? tags[0] : $"{group.Key} ({string.Join(", ", tags)})";
            prompt = OutsideDialogue(prompt, part => ReplaceName(part, group.Key, replacement));
        }

        var lines = subjects.Select(s =>
        {
            var name = string.IsNullOrWhiteSpace(s.Label) ? s.Tag : s.Label.Trim();
            return $"- {s.Tag} is {name}. Keep that identity, wardrobe, and look.";
        });

        var sameSubject = groups.Where(g => g.Count() > 1).Select(g =>
            $"{string.Join(", ", g.Select(s => s.Tag))} show different views of the same subject, {g.Key}. Preserve one identity; do not create a separate person or object for each photo.").ToArray();
        var identityNotes = sameSubject.Length == 0 ? "" : "\n" + string.Join("\n", sameSubject);
        return
            $"""
            Subject references from the previous film:
            {string.Join("\n", lines)}{identityNotes}

            {prompt}

            Whenever the prompt names one of those subjects, match the matching tagged still.
            """;
    }

    public static IReadOnlyList<(string Label, string Tag)> TagsFor(IEnumerable<string> refs, string? openingLabel = null)
    {
        var list = new List<(string Label, string Tag)>();
        var index = 1;
        if (!string.IsNullOrWhiteSpace(openingLabel))
        {
            list.Add((openingLabel, $"<Picture {index}>"));
            index++;
        }

        foreach (var item in refs)
        {
            if (index > MaxImages)
            {
                break;
            }

            var label = string.IsNullOrWhiteSpace(item) ? $"reference {index}" : item.Trim();
            list.Add((label, $"<Picture {index}>"));
            index++;
        }

        return list;
    }

    // Spoken words are immutable. A name in dialogue is speech, not a visual reference token.
    private static string OutsideDialogue(string prompt, Func<string,string> transform)
    {
        var result = new System.Text.StringBuilder(); var start = 0;
        foreach (System.Text.RegularExpressions.Match block in System.Text.RegularExpressions.Regex.Matches(prompt, @"<d>.*?</d>", System.Text.RegularExpressions.RegexOptions.Singleline)) {
            result.Append(transform(prompt[start..block.Index])).Append(block.Value);
            start = block.Index + block.Length;
        }
        return result.Append(transform(prompt[start..])).ToString();
    }

    private static string ReplaceName(string prompt, string name, string tag)
    {
        var at = "@" + name;
        prompt = prompt.Replace(at, tag, StringComparison.OrdinalIgnoreCase);
        // Whole-word "baker" → <Picture 1>, skip if already tagged.
        return System.Text.RegularExpressions.Regex.Replace(
            prompt,
            $@"(?<![\w<]){System.Text.RegularExpressions.Regex.Escape(name)}(?![\w>])",
            tag,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
