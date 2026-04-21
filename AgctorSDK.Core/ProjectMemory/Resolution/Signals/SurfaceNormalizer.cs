using System.Globalization;
using System.Text;

namespace AgctorSDK.Core.ProjectMemory.Resolution.Signals;

/// <summary>
/// Normalizes surface forms for lexical comparison: lowercase, diacritic-stripped, whitespace-collapsed.
/// Centralized so alias match, uniqueness, and mention index all agree.
/// </summary>
public static class SurfaceNormalizer
{
    public static string Normalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;

        var formD = s.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        bool prevSpace = false;
        foreach (var ch in formD)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsWhiteSpace(ch))
            {
                if (!prevSpace) sb.Append(' ');
                prevSpace = true;
                continue;
            }
            prevSpace = false;
            sb.Append(ch);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC).Trim();
    }
}
