using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgctorSDK.Host.Services.Scenarios;

/// <summary>Parses Ollama JSON for Phase 10 router (whitelist + optional confidence / maxTargets).</summary>
public static class ScenarioFlowRouterLlmParser
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static ScenarioFlowRouterLlmResult Parse(string raw, IEnumerable<string> allowedPersonaIds, ScenarioFlowRouterConfig config)
    {
        var allowed = new HashSet<string>(allowedPersonaIds, StringComparer.OrdinalIgnoreCase);
        var json = ExtractJsonObject(raw);
        RouterLlmDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<RouterLlmDto>(json, JsonOpts);
        }
        catch (Exception ex)
        {
            return ScenarioFlowRouterLlmResult.Fail($"Invalid router JSON: {ex.Message}");
        }

        if (dto == null)
            return ScenarioFlowRouterLlmResult.Fail("Empty router response.");

        if (!string.Equals(dto.SchemaVersion, "1.0", StringComparison.Ordinal))
            return ScenarioFlowRouterLlmResult.Fail($"Unsupported schemaVersion '{dto.SchemaVersion}'.");

        if (dto.NeedsClarification)
            return ScenarioFlowRouterLlmResult.Clarify(dto.ClarificationPrompt);

        var minConf = config.MinConfidence ?? 0;
        var list = new List<string>();
        foreach (var t in dto.Targets ?? new List<RouterLlmTargetDto>())
        {
            var pid = t.PersonaId?.Trim();
            if (string.IsNullOrEmpty(pid) || !allowed.Contains(pid))
                continue;
            if (t.Confidence is { } c && c < minConf)
                continue;
            list.Add(pid);
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();
        foreach (var p in list)
        {
            if (seen.Add(p))
                ordered.Add(p);
        }

        if (config.MaxTargets is { } cap && cap > 0 && ordered.Count > cap)
            ordered = ordered.Take(cap).ToList();

        if (ordered.Count > 0)
            return ScenarioFlowRouterLlmResult.Success(ordered);

        if (!string.IsNullOrWhiteSpace(config.FallbackPersonaId) && allowed.Contains(config.FallbackPersonaId))
            return ScenarioFlowRouterLlmResult.Success(new[] { config.FallbackPersonaId });

        return ScenarioFlowRouterLlmResult.Fail("Router returned no valid targets and no usable fallbackPersonaId.");
    }

    private static string ExtractJsonObject(string s)
    {
        var t = s.Trim();
        if (t.StartsWith("```", StringComparison.Ordinal))
        {
            var nl = t.IndexOf('\n');
            t = nl >= 0 ? t[(nl + 1)..] : "";
            var end = t.LastIndexOf("```", StringComparison.Ordinal);
            if (end > 0)
                t = t[..end];
        }

        t = t.Trim();
        var a = t.IndexOf('{');
        var b = t.LastIndexOf('}');
        if (a < 0 || b <= a)
            return t;
        return t.Substring(a, b - a + 1);
    }

    private sealed class RouterLlmDto
    {
        [JsonPropertyName("schemaVersion")]
        public string? SchemaVersion { get; set; }

        [JsonPropertyName("targets")]
        public List<RouterLlmTargetDto>? Targets { get; set; }

        [JsonPropertyName("needsClarification")]
        public bool NeedsClarification { get; set; }

        [JsonPropertyName("clarificationPrompt")]
        public string? ClarificationPrompt { get; set; }
    }

    private sealed class RouterLlmTargetDto
    {
        [JsonPropertyName("personaId")]
        public string? PersonaId { get; set; }

        [JsonPropertyName("confidence")]
        public double? Confidence { get; set; }
    }
}
