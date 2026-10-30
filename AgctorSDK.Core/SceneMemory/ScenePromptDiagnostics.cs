using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AgctorSDK.Core.SceneMemory;

public sealed record ScenePromptIssue(string Code, string Path, string Message, string? Subject,
    string? Property, string? Value, string? Target, string? Evidence, int? SourceStart, int? SourceLength, string Suggestion);

public sealed class ScenePromptValidationException : ArgumentException
{
    public IReadOnlyList<ScenePromptIssue> Issues { get; }
    public ScenePromptValidationException(IReadOnlyList<ScenePromptIssue> issues)
        : base(string.Join("\n", issues.Select(i => $"{i.Path}: {i.Message}"))) => Issues = issues;
}

/// <summary>Source-aware diagnostics shared by model repair, preview, and commit validation.</summary>
public static class ScenePromptDiagnostics
{
    public static ScenePromptIssue Issue(string prompt, string code, string path, string message,
        string? subject = null, string? property = null, string? value = null, string? target = null,
        string? evidence = null, string suggestion = "Correct this field using the original prompt, then preview again.")
    {
        var start = string.IsNullOrWhiteSpace(evidence) ? -1 : prompt.IndexOf(evidence, StringComparison.Ordinal);
        return new(code, path, message, subject, property, value, target, evidence,
            start < 0 ? null : start, start < 0 ? null : evidence!.Length, suggestion);
    }

    public static void ValidateFields(ScenePromptExtraction plan, IReadOnlyDictionary<string, SceneEntity> references, string prompt)
    {
        var issues = new List<ScenePromptIssue>();
        void Evidence(string? evidence, Action<string, string, string> add)
        {
            if (string.IsNullOrWhiteSpace(evidence))
                add("missing_evidence", "The model omitted the words supporting this change.", "Copy the exact supporting words from the current prompt into evidence.");
            else if (evidence.Length > 2000)
                add("evidence_too_long", "The supporting quote exceeds 2,000 characters.", "Use a shorter exact excerpt supporting this change.");
            else if (!prompt.Contains(evidence, StringComparison.Ordinal))
                add("evidence_mismatch", "The model's supporting quote was not found in the prompt.", "Use an exact excerpt from the current prompt. Do not paraphrase, invent text, or quote an older prompt.");
        }
        for (var index = 0; index < plan.Changes.Count; index++)
        {
            var c = plan.Changes[index];
            void Add(string code, string message, string suggestion) => issues.Add(Issue(prompt, code,
                $"changes[{index}]", message, c?.Subject, c?.Property, c?.Value, c?.Target, c?.Evidence, suggestion));
            if (c == null) { Add("missing_change", "This change is empty.", "Return a complete change object."); continue; }
            if (c.Subject == null || !references.ContainsKey(c.Subject)) Add("unknown_subject", $"Unknown subject reference '{c.Subject}'.", "Use an existing entity ID or declare the entity first.");
            if (c.Property == null || !Regex.IsMatch(c.Property, "^[a-z][a-z0-9_]{0,79}$")) Add("invalid_property", $"Invalid property '{c.Property}'.", "Use lowercase snake_case, beginning with a letter, up to 80 characters.");
            if (c.Value == null || c.Value.Length > 1000) Add("invalid_value", "The value is missing or exceeds 1,000 characters.", "Return a string value up to 1,000 characters; use an empty string for a relationship.");
            if (c.Target == null) Add("missing_target", "The target field is missing.", "Use a declared entity reference, or an empty string for an attribute.");
            Evidence(c.Evidence, Add);
        }
        var events = plan.Events ?? Array.Empty<ScenePromptEvent>();
        for (var index = 0; index < events.Count; index++)
        {
            var e = events[index];
            void Add(string code, string message, string suggestion) => issues.Add(Issue(prompt, code,
                $"events[{index}]", message, value: e?.Description, evidence: e?.Evidence, suggestion: suggestion));
            if (e == null) { Add("missing_event", "This event is empty.", "Return a complete event object."); continue; }
            if (string.IsNullOrWhiteSpace(e.Description) || e.Description.Length > 1000) Add("invalid_description", "The event description is missing or too long.", "Describe what happened in 1–1,000 characters.");
            if (e.Mode is not ("occurred" or "ongoing" or "intended" or "hypothetical" or "negated")) Add("invalid_event_mode", $"Unknown event mode '{e.Mode}'.", "Use occurred, ongoing, intended, hypothetical or negated without changing the meaning.");
            if (e.Participants == null || e.Participants.Count is < 1 or > 20 || e.Participants.Any(p => p == null || !references.ContainsKey(p)))
                Add("invalid_participants", "The event must identify 1–20 known participants.", "Use declared entity references or existing IDs for every participant.");
            Evidence(e.Evidence, Add);
        }
        if (issues.Count > 0) throw new ScenePromptValidationException(issues);
    }

    /// <summary>Only repair typography/whitespace when it identifies one exact original span.
    /// Never fuzzy-match paraphrases, case changes, punctuation changes, or missing words.</summary>
    public static string RestoreSourceQuote(string prompt, string evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence) || evidence.Length > 2000 || prompt.Contains(evidence, StringComparison.Ordinal)) return evidence;
        static (string Text, List<int> Starts, List<int> Ends) Canonical(string source)
        {
            var text = new StringBuilder(); var starts = new List<int>(); var ends = new List<int>();
            for (var i = 0; i < source.Length; i++)
            {
                var c = source[i];
                if (char.IsWhiteSpace(c))
                {
                    if (text.Length > 0 && text[text.Length - 1] == ' ') { ends[ends.Count - 1] = i + 1; continue; }
                    c = ' ';
                }
                else c = c switch { '\u2018' or '\u2019' => '\'', '\u201c' or '\u201d' => '"', _ => c };
                text.Append(c); starts.Add(i); ends.Add(i + 1);
            }
            return (text.ToString(), starts, ends);
        }
        var source = Canonical(prompt); var quote = Canonical(evidence).Text.Trim();
        if (quote.Length == 0) return evidence;
        var start = source.Text.IndexOf(quote, StringComparison.Ordinal);
        if (start < 0 || source.Text.IndexOf(quote, start + 1, StringComparison.Ordinal) >= 0) return evidence;
        var from = source.Starts[start]; var to = source.Ends[start + quote.Length - 1];
        return to - from <= 2000 ? prompt.Substring(from, to - from) : evidence;
    }
}
