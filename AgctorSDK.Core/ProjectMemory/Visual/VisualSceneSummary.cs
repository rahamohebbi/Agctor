using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Orchestration;
using AgctorSDK.Core.ProjectMemory.Visual.Models;

namespace AgctorSDK.Core.ProjectMemory.Visual;

/// <summary>
/// Scene text stored on catalog assets after vision extract (option 2) and used before query-time vision (option 1).
/// </summary>
public static class VisualSceneSummary
{
    public const int MinUsefulLength = 12;

    private static readonly Regex PhotoQuestion = new(
        @"(?i)\b(?:this|that|the|last|recent)\s+(?:photo|picture|image|pic|shot)\b|\b(?:photo|picture|image)\b.*\?|\bwhat(?:'s|\s+is|\s+am\s+i|\s+else\s+am\s+i)\s+(?:doing|happening|going\s+on)\b|\bwho(?:'s|\s+is)\s+in\b|\bwhat\s+do\s+you\s+see\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsPhotoRelatedQuestion(string? userMessage) =>
        !string.IsNullOrWhiteSpace(userMessage) && PhotoQuestion.IsMatch(userMessage);

    public static bool IsUseful(string? sceneSummary) =>
        !string.IsNullOrWhiteSpace(sceneSummary) && sceneSummary.Trim().Length >= MinUsefulLength;

    /// <summary>Pull optional sceneSummary from extract JSON without failing intent parse.</summary>
    public static string? TryParseFromExtractJson(string? rawLlmText)
    {
        var json = MemoryIntentJson.UnwrapMarkdownFences(rawLlmText ?? "");
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (!string.Equals(prop.Name, "sceneSummary", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (prop.Value.ValueKind == JsonValueKind.String)
                    return Normalize(prop.Value.GetString());
            }
        }
        catch
        {
            // ignore malformed JSON; extract parse will surface errors separately
        }

        return null;
    }

    /// <summary>Fallback when the model omits sceneSummary but returns observation intents.</summary>
    public static string? BuildFromIntents(IEnumerable<MemoryIntent>? intents)
    {
        if (intents == null)
            return null;

        var parts = intents
            .Where(i => i != null && !string.IsNullOrWhiteSpace(i.Value))
            .Select(i => i.Value!.Trim())
            .Where(v => v.Length >= 4)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();

        return parts.Count == 0 ? null : Normalize(string.Join("; ", parts));
    }

    public static bool HasSufficientSceneContext(PersonVisualContextResult? result)
    {
        if (result?.Assets == null || result.Assets.Count == 0)
            return false;

        return result.Assets.Any(a => IsUseful(a.SceneSummary));
    }

    /// <summary>Assets for query-time vision: same-turn attachments first, else newest catalog matches.</summary>
    public static IReadOnlyList<string> ResolveQueryAssetIds(
        bool hasAttachments,
        IEnumerable<string>? attachmentAssetIds,
        PersonVisualContextResult? visualResult,
        int maxAssets = 1)
    {
        var ids = new List<string>();
        if (hasAttachments && attachmentAssetIds != null)
        {
            foreach (var id in attachmentAssetIds)
            {
                if (string.IsNullOrWhiteSpace(id))
                    continue;
                var trimmed = id.Trim();
                if (!ids.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                    ids.Add(trimmed);
            }
        }

        if (ids.Count == 0 && visualResult?.Assets != null)
        {
            foreach (var asset in visualResult.Assets)
            {
                if (string.IsNullOrWhiteSpace(asset.AssetId))
                    continue;
                if (!ids.Contains(asset.AssetId, StringComparer.OrdinalIgnoreCase))
                    ids.Add(asset.AssetId);
                if (ids.Count >= Math.Clamp(maxAssets, 1, 3))
                    break;
            }
        }

        return ids;
    }

    public static bool ShouldUsePersonQueryVision(
        string personaId,
        string? userMessage,
        PersonVisualContextResult? visualResult,
        bool visionServicesAvailable)
    {
        if (!visionServicesAvailable)
            return false;
        if (!string.Equals(personaId, "person-query", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!IsPhotoRelatedQuestion(userMessage))
            return false;
        return !HasSufficientSceneContext(visualResult);
    }

    public static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        return text.Trim().Replace("\r\n", " ").Replace('\n', ' ');
    }
}
