using System;
using System.Text;
using System.Text.RegularExpressions;

namespace AgctorSDK.Core.ProjectMemory;

/// <summary>
/// Minimal glob for agent <c>memoryAccess</c> patterns (<c>*</c>, <c>**</c>).
/// </summary>
public static class GlobMatcher
{
    public static bool IsMatch(string relativePath, string pattern)
    {
        var p = NormalizePath(relativePath);
        var pat = NormalizePath(pattern);
        var rx = GlobToRegex(pat);
        return Regex.IsMatch(p, rx, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }

    public static bool AnyMatch(string relativePath, System.Collections.Generic.IEnumerable<string> patterns)
    {
        foreach (var g in patterns)
        {
            if (IsMatch(relativePath, g))
                return true;
        }

        return false;
    }

    private static string NormalizePath(string s)
    {
        return s.Replace('\\', '/').Trim('/');
    }

    private static string GlobToRegex(string glob)
    {
        var sb = new StringBuilder("^");
        for (var i = 0; i < glob.Length; i++)
        {
            if (i < glob.Length - 1 && glob[i] == '*' && glob[i + 1] == '*')
            {
                sb.Append(".*");
                i++;
                continue;
            }

            if (glob[i] == '*')
            {
                sb.Append("[^/]*");
                continue;
            }

            if (glob[i] == '?')
            {
                sb.Append('.');
                continue;
            }

            if (".^$+()[]{}|\\".IndexOf(glob[i]) >= 0)
            {
                sb.Append('\\').Append(glob[i]);
                continue;
            }

            sb.Append(glob[i]);
        }

        sb.Append('$');
        return sb.ToString();
    }
}
