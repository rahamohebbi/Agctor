using System.Text.Json;

namespace AgctorSDK.Host.Services.Scenarios;

/// <summary>Optional <c>Router</c> config (camelCase in JSON). Omitted fields = deterministic routing.</summary>
public sealed record ScenarioFlowRouterConfig(
    ScenarioFlowRouterMode Mode,
    int? MaxTargets,
    double? MinConfidence,
    string? FallbackPersonaId,
    string? LlmRoutingInstructions)
{
    /// <summary>Default when <c>config</c> is empty or <c>routerMode</c> is not <c>llm</c>.</summary>
    public static ScenarioFlowRouterConfig Default { get; } =
        new(ScenarioFlowRouterMode.Deterministic, null, null, null, null);

    public static ScenarioFlowRouterConfig Parse(JsonElement? config)
    {
        if (config is not { ValueKind: JsonValueKind.Object } el)
            return Default;

        var modeStr = el.TryGetProperty("routerMode", out var rm) && rm.ValueKind == JsonValueKind.String
            ? rm.GetString()
            : null;
        var mode = string.Equals(modeStr, "llm", StringComparison.OrdinalIgnoreCase)
            ? ScenarioFlowRouterMode.Llm
            : ScenarioFlowRouterMode.Deterministic;

        int? maxTargets = null;
        if (el.TryGetProperty("maxTargets", out var mt) && mt.ValueKind == JsonValueKind.Number && mt.TryGetInt32(out var mti) && mti > 0)
            maxTargets = mti;

        double? minConf = null;
        if (el.TryGetProperty("minConfidence", out var mc) && mc.ValueKind == JsonValueKind.Number && mc.TryGetDouble(out var mcd))
            minConf = Math.Clamp(mcd, 0, 1);

        string? fallback = el.TryGetProperty("fallbackPersonaId", out var fp) && fp.ValueKind == JsonValueKind.String
            ? fp.GetString()?.Trim()
            : null;

        if (string.IsNullOrWhiteSpace(fallback))
            fallback = null;

        string? llmInstr = el.TryGetProperty("llmRoutingInstructions", out var li) && li.ValueKind == JsonValueKind.String
            ? li.GetString()?.Trim()
            : null;
        if (string.IsNullOrWhiteSpace(llmInstr))
            llmInstr = null;

        return new ScenarioFlowRouterConfig(mode, maxTargets, minConf, fallback, llmInstr);
    }
}
