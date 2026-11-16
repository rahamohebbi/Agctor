using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace AgctorSDK.Core.SceneMemory;

/// <summary>Only explicit ranges support interpreted timing. No guessed durations or generated-video observations.</summary>
public static class SceneTimingEvidence
{
    private const string Time = @"(?:\d{1,2}:){1,2}\d{2}(?:\.\d+)?|\d+(?:\.\d+)?";
    private static readonly Regex Range = new(@"(?<![\d.:])(?<start>" + Time + @")\s*(?:(?<firstUnit>seconds?|secs?|s)\b)?\s*(?:[-–—]|to)\s*(?<end>" + Time + @")\s*(?:(?<lastUnit>seconds?|secs?|s)\b)?(?![\d.:])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    public static bool Supports(string evidence, SceneTimeRange timing)
    {
        timing.Validate();
        foreach (Match match in Range.Matches(evidence)) {
            var start = match.Groups["start"].Value;
            var end = match.Groups["end"].Value;
            if (!start.Contains(':') && !end.Contains(':') && !match.Groups["firstUnit"].Success && !match.Groups["lastUnit"].Success) continue;
            if (Seconds(start) == timing.StartSeconds && Seconds(end) == timing.EndSeconds) return true;
        }
        return false;
    }
    private static decimal? Seconds(string text)
    {
        decimal total = 0;
        var parts = text.Split(':');
        for (var i = 0; i < parts.Length; i++) {
            if (!decimal.TryParse(parts[i], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
                || value > 3600 || (i > 0 && value >= 60)) return null;
            total = total * 60 + value;
        }
        return total;
    }
}
