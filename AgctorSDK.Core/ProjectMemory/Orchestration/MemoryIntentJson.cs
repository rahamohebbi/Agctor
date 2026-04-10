using System;
using System.Text.Json;
using AgctorSDK.Core.ProjectMemory.Models;

namespace AgctorSDK.Core.ProjectMemory.Orchestration;

/// <summary>
/// Parses extractor LLM output into <see cref="MemoryIntentBatch"/>; strips common markdown fences.
/// </summary>
public static class MemoryIntentJson
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>Remove leading ```json / ``` wrapper if present.</summary>
    public static string UnwrapMarkdownFences(string text)
    {
        var t = (text ?? "").Trim();
        if (t.Length < 3 || !t.StartsWith("```", StringComparison.Ordinal))
            return t;

        var firstNl = t.IndexOf('\n');
        if (firstNl < 0)
            return t;
        var rest = t[(firstNl + 1)..];
        var end = rest.LastIndexOf("```", StringComparison.Ordinal);
        return end > 0 ? rest[..end].Trim() : rest.Trim();
    }

    /// <summary>Try deserialize <see cref="MemoryIntentBatch"/> after unwrapping fences.</summary>
    public static bool TryParseBatch(string rawLlmText, out MemoryIntentBatch? batch, out string? error)
    {
        batch = null;
        error = null;
        var json = UnwrapMarkdownFences(rawLlmText);
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "Empty extractor output.";
            return false;
        }

        try
        {
            batch = JsonSerializer.Deserialize<MemoryIntentBatch>(json, JsonOptions);
            if (batch?.MemoryIntents == null)
            {
                error = "Missing memoryIntents array.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
