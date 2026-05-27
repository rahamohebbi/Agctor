using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AgctorSDK.Core.ProjectMemory.Visual.Models;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Core.ProjectMemory.Visual;

/// <summary>Shared parameter parsing for PRD-023 visual <see cref="IToolActor"/> tools.</summary>
public static class VisualToolParams
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static string ToJson<T>(T value) => JsonSerializer.Serialize(value, JsonOpts);

    public static string? GetString(IDictionary<string, object>? values, string key)
    {
        if (values == null || !values.TryGetValue(key, out var value) || value == null)
            return null;
        return value switch
        {
            string s => s,
            JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString(),
            JsonElement je => je.ToString(),
            _ => value.ToString()
        };
    }

    public static long GetInt64(IDictionary<string, object>? values, string key, long defaultValue = 0)
    {
        var s = GetString(values, key);
        if (string.IsNullOrWhiteSpace(s))
            return defaultValue;
        return long.TryParse(s, out var n) ? n : defaultValue;
    }

    public static int GetInt32(IDictionary<string, object>? values, string key, int defaultValue = 0)
    {
        var s = GetString(values, key);
        if (string.IsNullOrWhiteSpace(s))
            return defaultValue;
        return int.TryParse(s, out var n) ? n : defaultValue;
    }

    public static string ResolveProjectRoot(IDictionary<string, object>? values)
    {
        var fromParam = GetString(values, "projectRoot");
        if (!string.IsNullOrWhiteSpace(fromParam))
            return Path.GetFullPath(fromParam.Trim());

        try
        {
            var root = ProjectMemoryServiceAccessor
                .GetRequiredService<IOptions<ProjectMemoryAgentOptions>>()
                .Value.ProjectRoot;
            if (!string.IsNullOrWhiteSpace(root))
                return Path.GetFullPath(root.Trim());
        }
        catch (InvalidOperationException)
        {
            // fall through
        }

        throw new InvalidOperationException("projectRoot parameter required when DI is not initialized.");
    }

    public static string RequireScenarioId(IDictionary<string, object>? values)
    {
        var scenarioId = GetString(values, "scenarioId");
        if (string.IsNullOrWhiteSpace(scenarioId))
            throw new InvalidOperationException("scenarioId is required.");
        return PersonaScenarioScope.SanitizeFolderSegment(scenarioId);
    }

    public static string RequireAssetId(IDictionary<string, object>? values)
    {
        var assetId = GetString(values, "assetId");
        if (string.IsNullOrWhiteSpace(assetId))
            throw new InvalidOperationException("assetId is required.");
        return assetId.Trim();
    }

    public static List<VisualAssetSubject>? ParseSubjects(IDictionary<string, object>? values, string key = "subjects")
    {
        if (values == null || !values.TryGetValue(key, out var raw) || raw == null)
            return null;

        if (raw is JsonElement je)
            return JsonSerializer.Deserialize<List<VisualAssetSubject>>(je.GetRawText(), JsonOpts);

        var text = raw.ToString();
        if (string.IsNullOrWhiteSpace(text))
            return null;
        return JsonSerializer.Deserialize<List<VisualAssetSubject>>(text, JsonOpts);
    }
}
