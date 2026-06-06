using System.Text.Json;

namespace AgctorSDK.Core.ProjectMemory.Scenarios;

/// <summary>Evaluates Gate nodes against runtime fact store (PRD-024).</summary>
public static class ScenarioFlowGateEvaluator
{
    public static bool Evaluate(JsonElement? config, IReadOnlyDictionary<string, object?> facts)
    {
        if (config == null || config.Value.ValueKind != JsonValueKind.Object)
            return false;

        var root = config.Value;
        var factKey = root.TryGetProperty("fact", out var f) ? f.GetString()?.Trim() : null;
        if (string.IsNullOrEmpty(factKey))
            return false;

        facts.TryGetValue(factKey, out var factValue);
        var op = root.TryGetProperty("operator", out var o) ? o.GetString()?.Trim() : "isTrue";

        return op switch
        {
            "isTrue" => IsTruthy(factValue),
            "isFalse" => !IsTruthy(factValue),
            "equals" => EqualsFact(factValue, root),
            "gt" => CompareNumeric(factValue, root, (a, b) => a > b),
            "lt" => CompareNumeric(factValue, root, (a, b) => a < b),
            _ => false
        };
    }

    public static string? ResolveBranchEdgeId(JsonElement? config, bool conditionTrue)
    {
        if (config == null || config.Value.ValueKind != JsonValueKind.Object)
            return null;

        var root = config.Value;
        var key = conditionTrue ? "trueEdgeId" : "falseEdgeId";
        return root.TryGetProperty(key, out var e) ? e.GetString()?.Trim() : null;
    }

    private static bool IsTruthy(object? value) => value switch
    {
        null => false,
        bool b => b,
        string s => !string.IsNullOrWhiteSpace(s) && !string.Equals(s, "false", StringComparison.OrdinalIgnoreCase),
        int i => i != 0,
        long l => l != 0,
        double d => Math.Abs(d) > double.Epsilon,
        _ => true
    };

    private static bool EqualsFact(object? factValue, JsonElement config)
    {
        if (!config.TryGetProperty("value", out var expected))
            return factValue == null;

        return expected.ValueKind switch
        {
            JsonValueKind.String => string.Equals(factValue?.ToString(), expected.GetString(), StringComparison.OrdinalIgnoreCase),
            JsonValueKind.True => factValue is true,
            JsonValueKind.False => factValue is false,
            JsonValueKind.Number when factValue is IConvertible c =>
                Math.Abs(Convert.ToDouble(c, System.Globalization.CultureInfo.InvariantCulture)
                         - expected.GetDouble()) < 0.0001,
            _ => string.Equals(factValue?.ToString(), expected.ToString(), StringComparison.OrdinalIgnoreCase)
        };
    }

    private static bool CompareNumeric(object? factValue, JsonElement config, Func<double, double, bool> cmp)
    {
        if (!config.TryGetProperty("value", out var expected) || expected.ValueKind != JsonValueKind.Number)
            return false;
        if (factValue is not IConvertible convertible)
            return false;

        var left = Convert.ToDouble(convertible, System.Globalization.CultureInfo.InvariantCulture);
        return cmp(left, expected.GetDouble());
    }
}
