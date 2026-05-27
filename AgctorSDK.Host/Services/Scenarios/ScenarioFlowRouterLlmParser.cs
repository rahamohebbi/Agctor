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
        var scored = new List<(string PersonaId, double Confidence)>();
        foreach (var t in dto.Targets ?? new List<RouterLlmTargetDto>())
        {
            var pid = t.PersonaId?.Trim();
            if (string.IsNullOrEmpty(pid) || !allowed.Contains(pid))
                continue;
            var conf = t.Confidence ?? 0;
            if (conf < minConf)
                continue;
            scored.Add((pid, conf));
        }

        // One row per persona — keep highest confidence if the model duplicated ids.
        var ordered = scored
            .GroupBy(x => x.PersonaId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.Confidence).First())
            .OrderByDescending(x => x.Confidence)
            .ThenBy(x => x.PersonaId, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.PersonaId)
            .ToList();

        if (config.EffectiveMaxTargets is { } cap && cap > 0 && ordered.Count > cap)
            ordered = ordered.Take(cap).ToList();

        if (ordered.Count > 0)
        {
            ScenarioFlowRouterBranchExecution? branchMode = null;
            if (config.TargetPolicy == ScenarioFlowRouterTargetPolicy.AllMatching
                && config.BranchExecution == ScenarioFlowRouterBranchExecution.Auto)
            {
                branchMode = ParseBranchExecutionMode(dto)
                             ?? ScenarioFlowBranchExecutionPlanner.InferAuto(ordered);
            }

            return ScenarioFlowRouterLlmResult.Success(ordered, branchMode);
        }

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

    private static ScenarioFlowRouterBranchExecution? ParseBranchExecutionMode(RouterLlmDto dto)
    {
        var raw = dto.BranchExecutionMode?.Trim().ToLowerInvariant();
        return raw switch
        {
            "sequential" or "sequence" or "serial" => ScenarioFlowRouterBranchExecution.Sequential,
            "parallel" or "concurrent" => ScenarioFlowRouterBranchExecution.Parallel,
            _ => null
        };
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

        [JsonPropertyName("branchExecutionMode")]
        public string? BranchExecutionMode { get; set; }
    }

    private sealed class RouterLlmTargetDto
    {
        [JsonPropertyName("personaId")]
        public string? PersonaId { get; set; }

        [JsonPropertyName("confidence")]
        public double? Confidence { get; set; }
    }
}
