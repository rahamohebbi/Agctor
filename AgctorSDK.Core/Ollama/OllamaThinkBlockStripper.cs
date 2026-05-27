using System;
using System.Text.RegularExpressions;

namespace AgctorSDK.Core.Ollama;

/// <summary>Removes Gemma / Qwen style thinking blocks before JSON parsing (PRD-023 §8).</summary>
public static class OllamaThinkBlockStripper
{
    private static readonly Regex ThinkTagBlock = new(
        @"<\|think\|>[\s\S]*?<\|/think\|>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Strip(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        var t = ThinkTagBlock.Replace(text, "");
        t = StripDelimitedBlock(t, "\u003Credacted_thinking\u003E", "\u003C/redacted_thinking\u003E");
        return t.Trim();
    }

    private static string StripDelimitedBlock(string text, string open, string close)
    {
        var start = 0;
        while (true)
        {
            var i = text.IndexOf(open, start, StringComparison.OrdinalIgnoreCase);
            if (i < 0)
                break;
            var j = text.IndexOf(close, i + open.Length, StringComparison.OrdinalIgnoreCase);
            if (j < 0)
                break;
            text = text.Remove(i, j + close.Length - i);
            start = i;
        }

        return text;
    }
}
