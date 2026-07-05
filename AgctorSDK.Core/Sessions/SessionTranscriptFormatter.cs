using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AgctorSDK.Core.Sessions.Models;

namespace AgctorSDK.Core.Sessions;

/// <summary>
/// Builds a plain-text transcript prefix from session turns for ProjectMemory ingest prompts.
/// </summary>
public static class SessionTranscriptFormatter
{
    /// <summary>Default max prior turns when config is unset.</summary>
    public const int DefaultMaxConversationTurns = PlaygroundChatSettings.DefaultMaxConversationTurns;

    private static readonly Regex RetryFollowUp = new(
        @"^\s*(try again|retry|please retry|once more|do it again|again)\s*\.?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex BareYesNo = new(
        @"^\s*(yes|no|y|n)\s*\.?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Maps short retry phrases to the last substantive user turn so routing and agents do not need the question re-typed.
    /// </summary>
    public static string ExpandFollowUpFromHistory(string userMessage, IReadOnlyList<SessionTurn>? priorTurns)
    {
        var text = userMessage?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(text) || priorTurns == null || priorTurns.Count == 0)
            return text;
        if (!RetryFollowUp.IsMatch(text))
            return text;

        var last = FindLastSubstantiveUserMessage(priorTurns);
        return string.IsNullOrWhiteSpace(last) ? text : last;
    }

    /// <summary>Most recent user turn that is not a bare retry or yes/no.</summary>
    public static string? FindLastSubstantiveUserMessage(IReadOnlyList<SessionTurn>? priorTurns)
    {
        if (priorTurns == null || priorTurns.Count == 0)
            return null;

        foreach (var t in priorTurns.OrderByDescending(x => x.Sequence))
        {
            if (t.Role != SessionRole.User)
                continue;
            var content = t.Content?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(content))
                continue;
            if (RetryFollowUp.IsMatch(content) || BareYesNo.IsMatch(content))
                continue;
            return content;
        }

        return null;
    }

    /// <summary>Keeps the most recent turns by <see cref="SessionTurn.Sequence"/>.</summary>
    public static IReadOnlyList<SessionTurn> TakeRecentTurns(
        IReadOnlyList<SessionTurn>? turns,
        int maxTurns = DefaultMaxConversationTurns)
    {
        if (turns == null || turns.Count == 0)
            return Array.Empty<SessionTurn>();
        if (maxTurns <= 0 || turns.Count <= maxTurns)
            return turns;
        return turns.OrderBy(x => x.Sequence).TakeLast(maxTurns).ToList();
    }

    /// <summary>Turns for chat prompts: drop the in-flight turn group, then cap.</summary>
    public static IReadOnlyList<SessionTurn> ForPromptContext(
        IReadOnlyList<SessionTurn>? turns,
        string? excludeTurnGroupId = null,
        int maxTurns = DefaultMaxConversationTurns)
    {
        IEnumerable<SessionTurn> slice = turns ?? Array.Empty<SessionTurn>();
        if (!string.IsNullOrWhiteSpace(excludeTurnGroupId))
        {
            slice = slice.Where(t =>
                !string.Equals(t.TurnGroupId, excludeTurnGroupId.Trim(), StringComparison.Ordinal));
        }

        return TakeRecentTurns(slice.ToList(), maxTurns);
    }

    /// <summary>User/assistant lines ordered by <see cref="SessionTurn.Sequence"/>.</summary>
    /// <param name="maxTurns">When set, only the most recent turns are included.</param>
    public static string? BuildPrefix(IReadOnlyList<SessionTurn>? turns, int? maxTurns = null)
    {
        var slice = maxTurns.HasValue ? TakeRecentTurns(turns, maxTurns.Value) : turns;
        if (slice == null || slice.Count == 0)
            return null;

        var sb = new StringBuilder();
        foreach (var t in slice.OrderBy(x => x.Sequence))
        {
            if (t.Role is SessionRole.System or SessionRole.Tool)
                continue;

            var label = t.Role == SessionRole.User ? "User" : "Assistant";
            sb.Append(label).Append(": ").Append(t.Content).Append('\n');
        }

        return sb.Length == 0 ? null : sb.ToString();
    }
}
