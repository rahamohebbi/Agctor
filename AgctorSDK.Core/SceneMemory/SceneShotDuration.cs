using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace AgctorSDK.Core.SceneMemory;

/// <summary>Reads explicit clip-length instructions, never timings of actions within a scene.</summary>
public static class SceneShotDuration
{
    private const string Amount = @"(?<amount>\d+(?:\.\d+)?)\s*[-\u2010-\u2014]?\s*(?<unit>seconds?|secs?|s|minutes?|mins?|m)\b";
    private const string Clip = @"(?:video|clip|shot|film|animation)\b";
    private static readonly Regex Instructions = new(
        @"(?<![\p{L}\p{N}.])(?:" + Amount + @"[\s-]+(?:(?:cinematic|animated|realistic|live-action|short|long|vertical|horizontal|silent|3D|cartoon)\s+){0,5}" + Clip +
        @"|\b" + Clip + @"\s+(?:(?:should\s+)?(?:lasts?|be|runs?(?:\s+for)?|of)\s+)?" + Amount +
        @"|\b(?:total\s+)?(?:duration|clip\s+length|video\s+length)\s*(?:is|of|:|=)?\s*" + Amount + @")",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static double? Resolve(double? plannedSeconds, string prompt)
    {
        // Explicit metadata, including an edited timeline duration, takes priority.
        if (plannedSeconds.HasValue) return plannedSeconds;
        var values = Instructions.Matches(prompt).Cast<Match>().Select(m =>
            double.Parse(m.Groups["amount"].Value, CultureInfo.InvariantCulture)
            * (m.Groups["unit"].Value.StartsWith("m", StringComparison.OrdinalIgnoreCase) ? 60 : 1))
            .Distinct().ToArray();
        if (values.Length > 1)
            throw new ArgumentException("This prompt specifies more than one clip duration. Set the shot's planned duration explicitly or use one clip length in the prompt.");
        return values.Length == 1 ? values[0] : null;
    }
}
