using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AgctorSDK.Core.ProjectMemory.Visual.Models;

namespace AgctorSDK.Core.ProjectMemory.Visual;

/// <summary>Heuristic subject linking from captions like "this is me Raha" (no Ollama required).</summary>
public static class VisualMessageIdentityHints
{
    private static readonly Regex ThisIsMe = new(
        @"(?i)\b(?:this\s+is(?:\s+me)?|that(?:'s|s)\s+me|it(?:'s|s)\s+me)\s+([A-Za-z][A-Za-z0-9_-]*)",
        RegexOptions.Compiled);

    private static readonly Regex IAm = new(
        @"(?i)\bI(?:'m|\s+am)\s+([A-Za-z][A-Za-z0-9_-]*)",
        RegexOptions.Compiled);

    /// <summary>Apply caption/focus hints to the asset when subjects are not yet tagged.</summary>
    public static bool TryApplyToRecord(
        VisualAssetRecord record,
        string? userMessage,
        string? focusEntityKey,
        string? projectRoot,
        string? scenarioId)
    {
        if (record == null)
            return false;

        var keys = CollectEntityKeys(userMessage, focusEntityKey, projectRoot, scenarioId);
        if (keys.Count == 0)
            return false;

        if (record.Subjects.Count == 0)
        {
            record.Subjects = keys
                .Select((k, i) => new VisualAssetSubject
                {
                    EntityKey = k,
                    Role = i == 0 ? "primary" : "secondary",
                    DisplayName = i == 0 ? TitleCase(k) : null
                })
                .ToList();
        }

        record.Inference ??= new VisualAssetInference();
        record.Inference.Source = "caption";
        record.Inference.Confidence = Math.Max(record.Inference.Confidence, 0.88);
        record.Inference.EntityKeys = keys.ToList();
        record.Inference.Rationale = "Linked from your message (this is me / project focus).";

        if (string.IsNullOrWhiteSpace(record.Context.UserCaption) && !string.IsNullOrWhiteSpace(userMessage))
            record.Context.UserCaption = userMessage.Trim();

        if (string.Equals(record.State, VisualAssetStates.Uploaded, StringComparison.OrdinalIgnoreCase))
            record.State = VisualAssetStates.ReadyForExtract;

        return true;
    }

    public static IReadOnlyList<string> CollectEntityKeys(
        string? userMessage,
        string? focusEntityKey,
        string? projectRoot,
        string? scenarioId)
    {
        var keys = new List<string>();
        if (!string.IsNullOrWhiteSpace(focusEntityKey))
            AddKey(keys, focusEntityKey);

        var text = userMessage?.Trim() ?? "";
        if (text.Length > 0)
        {
            var m1 = ThisIsMe.Match(text);
            if (m1.Success)
                AddKey(keys, m1.Groups[1].Value);

            var m2 = IAm.Match(text);
            if (m2.Success)
                AddKey(keys, m2.Groups[1].Value);

            if (keys.Count == 0 && !string.IsNullOrWhiteSpace(projectRoot) && !string.IsNullOrWhiteSpace(scenarioId))
            {
                var peopleDir = Path.Combine(
                    PersonaScenarioScope.GetEntityWorkspaceRoot(projectRoot, scenarioId),
                    "people");
                if (Directory.Exists(peopleDir))
                {
                    foreach (var dir in Directory.EnumerateDirectories(peopleDir))
                    {
                        var slug = Path.GetFileName(dir);
                        if (slug.Length > 0 && text.Contains(slug, StringComparison.OrdinalIgnoreCase))
                            AddKey(keys, slug);
                    }
                }
            }
        }

        return keys;
    }

    private static void AddKey(List<string> keys, string raw)
    {
        var slug = PersonaScenarioScope.SanitizeFolderSegment(raw).ToLowerInvariant();
        if (slug.Length == 0)
            return;
        if (!keys.Contains(slug, StringComparer.OrdinalIgnoreCase))
            keys.Add(slug);
    }

    private static string TitleCase(string slug) =>
        string.IsNullOrEmpty(slug) ? slug : char.ToUpperInvariant(slug[0]) + slug[1..];
}
