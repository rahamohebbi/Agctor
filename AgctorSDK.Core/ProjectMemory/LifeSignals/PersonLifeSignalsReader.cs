using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace AgctorSDK.Core.ProjectMemory.LifeSignals;

/// <summary>
/// Read-only scan of scenario-scoped people markdown for lightweight "daily life" nudges
/// (upcoming birthdays, stale contact, recent timeline activity).
/// </summary>
public static class PersonLifeSignalsReader
{
    private static readonly Regex BirthdayLine = new(
        @"(?im)^\s*[-*]?\s*(birthday|date\s*of\s*birth|born)\s*[:\s]+(.+)$",
        RegexOptions.Compiled);

    private static readonly Regex TimelineDateLine = new(
        @"^\s*[-*]\s*(\d{4}-\d{2}-\d{2}|\d{1,2}[/-]\d{1,2}(?:[/-]\d{2,4})?)\s*[:\-–]\s*(.+)$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>Scans <c>scenarios/&lt;id&gt;/people/*</c> (or project-root <c>people/</c> when scenario is empty).</summary>
    public static IReadOnlyList<PersonLifeSignal> Scan(
        string projectRoot,
        string? scenarioId,
        DateTime? asOfUtc = null,
        int staleContactDays = 30,
        int birthdayHorizonDays = 14)
    {
        var signals = new List<PersonLifeSignal>();
        if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
            return signals;

        var workspace = PersonaScenarioScope.GetEntityWorkspaceRoot(projectRoot, scenarioId);
        var peopleDir = Path.Combine(workspace, "people");
        if (!Directory.Exists(peopleDir))
            return signals;

        var today = (asOfUtc ?? DateTime.UtcNow).Date;

        foreach (var entityDir in Directory.EnumerateDirectories(peopleDir))
        {
            var entityKey = Path.GetFileName(entityDir);
            if (string.IsNullOrWhiteSpace(entityKey) || entityKey.StartsWith('.'))
                continue;

            var displayName = ReadDisplayName(entityDir, entityKey);
            AppendBirthdaySignals(signals, entityKey, displayName, entityDir, today, birthdayHorizonDays);
            AppendContactSignals(signals, entityKey, displayName, entityDir, today, staleContactDays);
        }

        return signals
            .OrderBy(s => s.Priority)
            .ThenBy(s => s.DaysUntil ?? int.MaxValue)
            .ThenBy(s => s.EntityKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ReadDisplayName(string entityDir, string entityKey)
    {
        var profilePath = Path.Combine(entityDir, "profile.md");
        if (!File.Exists(profilePath))
            return entityKey;

        var text = File.ReadAllText(profilePath);
        var nameMatch = Regex.Match(text, @"(?im)^\s*[-*]?\s*name\s*:\s*(.+)$");
        if (nameMatch.Success)
            return nameMatch.Groups[1].Value.Trim();

        var basic = Regex.Match(text, @"(?is)##\s*Basic\s+Info\s*(.+?)(?=##|\z)");
        if (basic.Success)
        {
            var line = basic.Groups[1].Value.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim().TrimStart('-', '*', ' '))
                .FirstOrDefault(l => l.Length > 2 && !l.StartsWith('#'));
            if (!string.IsNullOrWhiteSpace(line))
                return line;
        }

        return entityKey;
    }

    private static void AppendBirthdaySignals(
        List<PersonLifeSignal> signals,
        string entityKey,
        string displayName,
        string entityDir,
        DateTime today,
        int horizonDays)
    {
        var profilePath = Path.Combine(entityDir, "profile.md");
        if (!File.Exists(profilePath))
            return;

        var text = File.ReadAllText(profilePath);
        foreach (Match m in BirthdayLine.Matches(text))
        {
            var raw = m.Groups[2].Value.Trim();
            if (!TryParseMonthDay(raw, out var month, out var day))
                continue;

            var next = NextOccurrence(today, month, day);
            var days = (next - today).Days;
            if (days > horizonDays)
                continue;

            signals.Add(new PersonLifeSignal
            {
                EntityKey = entityKey,
                DisplayName = displayName,
                Kind = "birthday_upcoming",
                Message = days == 0
                    ? $"{displayName}'s birthday is today."
                    : $"{displayName}'s birthday is in {days} day(s) ({next:MMM d}).",
                DaysUntil = days,
                Priority = days <= 3 ? 0 : 1
            });
            break;
        }
    }

    private static void AppendContactSignals(
        List<PersonLifeSignal> signals,
        string entityKey,
        string displayName,
        string entityDir,
        DateTime today,
        int staleContactDays)
    {
        var timelinePath = Path.Combine(entityDir, "timeline.md");
        if (!File.Exists(timelinePath))
        {
            signals.Add(new PersonLifeSignal
            {
                EntityKey = entityKey,
                DisplayName = displayName,
                Kind = "no_timeline",
                Message = $"No timeline yet for {displayName} — log a quick interaction to track contact.",
                Priority = 3
            });
            return;
        }

        var text = File.ReadAllText(timelinePath);
        DateTime? last = null;
        string? lastSnippet = null;
        foreach (Match m in TimelineDateLine.Matches(text))
        {
            if (!TryParseTimelineDate(m.Groups[1].Value.Trim(), out var dt))
                continue;
            if (last == null || dt > last)
            {
                last = dt;
                lastSnippet = m.Groups[2].Value.Trim();
            }
        }

        if (last == null)
            return;

        var daysSince = (today - last.Value.Date).Days;
        if (daysSince < staleContactDays)
            return;

        var snippet = string.IsNullOrWhiteSpace(lastSnippet) ? "" : $" Last note: \"{Truncate(lastSnippet, 80)}\".";
        signals.Add(new PersonLifeSignal
        {
            EntityKey = entityKey,
            DisplayName = displayName,
            Kind = "stale_contact",
            Message = $"You have not logged contact with {displayName} in {daysSince} days.{snippet}",
            DaysUntil = null,
            Priority = 2
        });
    }

    private static bool TryParseMonthDay(string raw, out int month, out int day)
    {
        month = day = 0;
        raw = raw.Trim().TrimEnd('.');
        // Curator output often uses ordinals ("22nd May 1980"); strip them before TryParse.
        var normalized = Regex.Replace(raw, @"(\d{1,2})(?:st|nd|rd|th)\b", "$1", RegexOptions.IgnoreCase).Trim();

        foreach (var culture in new[] { CultureInfo.InvariantCulture, CultureInfo.CurrentCulture })
        {
            if (DateTime.TryParse(normalized, culture, DateTimeStyles.AllowWhiteSpaces, out var full))
            {
                month = full.Month;
                day = full.Day;
                return true;
            }
        }

        var ofPattern = Regex.Match(normalized, @"(?i)(\d{1,2})\s+of\s+([A-Za-z]+)");
        if (ofPattern.Success
            && DateTime.TryParse($"{ofPattern.Groups[1].Value} {ofPattern.Groups[2].Value}", CultureInfo.InvariantCulture, DateTimeStyles.None, out var ofDate))
        {
            month = ofDate.Month;
            day = ofDate.Day;
            return true;
        }

        // "22 May" or "22 May 1980" after ordinal normalization.
        var dayMonth = Regex.Match(normalized, @"(?i)^(\d{1,2})\s+([A-Za-z]+)(?:\s+\d{2,4})?$");
        if (dayMonth.Success
            && DateTime.TryParse($"{dayMonth.Groups[1].Value} {dayMonth.Groups[2].Value}", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dmDate))
        {
            month = dmDate.Month;
            day = dmDate.Day;
            return true;
        }

        return false;
    }

    private static bool TryParseTimelineDate(string raw, out DateTime dt)
    {
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out dt))
            return true;
        return DateTime.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out dt);
    }

    private static DateTime NextOccurrence(DateTime today, int month, int day)
    {
        var year = today.Year;
        var candidate = SafeDate(year, month, day);
        if (candidate < today)
            candidate = SafeDate(year + 1, month, day);
        return candidate;
    }

    private static DateTime SafeDate(int year, int month, int day)
    {
        var dim = DateTime.DaysInMonth(year, month);
        return new DateTime(year, month, Math.Min(day, dim), 0, 0, 0, DateTimeKind.Utc).Date;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";
}

/// <summary>One proactive hint for the dashboard or chat sidebar.</summary>
public sealed class PersonLifeSignal
{
    public string EntityKey { get; set; } = "";
    public string DisplayName { get; set; } = "";
    /// <summary>birthday_upcoming | stale_contact | no_timeline</summary>
    public string Kind { get; set; } = "";
    public string Message { get; set; } = "";
    public int? DaysUntil { get; set; }
    /// <summary>Lower sorts earlier (more urgent).</summary>
    public int Priority { get; set; }
}
