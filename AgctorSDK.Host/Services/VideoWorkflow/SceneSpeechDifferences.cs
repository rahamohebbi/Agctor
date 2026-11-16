using System.Text.RegularExpressions;

namespace AgctorSDK.Host.Services.VideoWorkflow;

/// <summary>Aligns requested and recognized words without changing the source transcript.</summary>
public static class SceneSpeechDifferences
{
    public static IReadOnlyList<SceneSpeechIssue> Find(string expected, string transcript)
    {
        var wanted = Regex.Matches(expected, @"[\p{L}\p{N}]+(?:['’][\p{L}\p{N}]+)*");
        var heard = Regex.Matches(transcript, @"[\p{L}\p{N}]+(?:['’][\p{L}\p{N}]+)*");
        // Bound the alignment work. The full expected text still remains available to the user.
        if (wanted.Count > 500 || heard.Count > 500) return [];
        string Key(Match word) => word.Value.Replace("'", "").Replace("’", "").ToLowerInvariant();
        var lengths = new int[wanted.Count + 1, heard.Count + 1];
        for (var i = wanted.Count - 1; i >= 0; i--)
            for (var j = heard.Count - 1; j >= 0; j--)
                lengths[i, j] = Key(wanted[i]) == Key(heard[j]) ? lengths[i + 1, j + 1] + 1 : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
        var issues = new List<SceneSpeechIssue>();
        var a = 0; var b = 0;
        while (a < wanted.Count || b < heard.Count) {
            if (a < wanted.Count && b < heard.Count && Key(wanted[a]) == Key(heard[b])) { a++; b++; continue; }
            var first = b;
            var missing = new List<string>();
            while (a < wanted.Count || b < heard.Count) {
                if (a < wanted.Count && b < heard.Count && Key(wanted[a]) == Key(heard[b])) break;
                if (a < wanted.Count && (b == heard.Count || lengths[a + 1, b] >= lengths[a, b + 1])) missing.Add(wanted[a++].Value);
                else b++;
            }
            var start = first < heard.Count ? heard[first].Index : transcript.Length;
            var length = b > first ? heard[b - 1].Index + heard[b - 1].Length - start : 0;
            var reason = missing.Count > 0
                ? (length == 0 ? "Missing expected words: " : "Expected words: ") + string.Join(" ", missing)
                : "These words were not in the requested dialogue.";
            issues.Add(new(start, length, reason));
        }
        return issues;
    }
}
