using System.Text.RegularExpressions;

namespace AgctorSDK.Host.Services.Scenarios;

/// <summary>
/// Evaluates <see cref="ScenarioFlowEdge"/> rules for deterministic <c>Router</c> branches.
/// Empty <see cref="ScenarioFlowEdge.Condition"/> marks the default branch and is not evaluated here.
/// </summary>
public static class ScenarioFlowDeterministicRouting
{
    /// <summary>True when <paramref name="edge"/> has a non-empty condition and it matches <paramref name="userMessage"/>.</summary>
    public static bool Matches(string userMessage, ScenarioFlowEdge edge)
    {
        var pattern = edge.Condition?.Trim();
        if (string.IsNullOrEmpty(pattern))
            return false;

        var msg = userMessage ?? "";
        var kind = (edge.ConditionMatch ?? "contains").Trim();

        if (string.Equals(kind, "contains", StringComparison.OrdinalIgnoreCase))
            return msg.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;

        if (string.Equals(kind, "equals", StringComparison.OrdinalIgnoreCase))
            return string.Equals(msg.Trim(), pattern, StringComparison.OrdinalIgnoreCase);

        if (string.Equals(kind, "startsWith", StringComparison.OrdinalIgnoreCase))
            return msg.TrimStart().StartsWith(pattern, StringComparison.OrdinalIgnoreCase);

        if (string.Equals(kind, "endsWith", StringComparison.OrdinalIgnoreCase))
            return msg.TrimEnd().EndsWith(pattern, StringComparison.OrdinalIgnoreCase);

        if (string.Equals(kind, "regex", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return Regex.IsMatch(msg, pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        return msg.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
