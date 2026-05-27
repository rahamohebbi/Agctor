using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgctorSDK.Core.Tools.Models;

namespace AgctorSDK.Core.Utils.ActivityTracking;

/// <summary>JSON payloads for tool spans in the dashboard trace timeline.</summary>
public static class ToolTraceTimelineDetail
{
    public const int MaxOutputPreviewChars = 12_288;
    public const int MaxParamValueChars = 480;
    public const int MaxParams = 24;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string BuildInvokeJson(
        string toolId,
        string operation,
        IDictionary<string, object>? parameters,
        ToolResult? result,
        string? invokingAgentId = null,
        Exception? exception = null)
    {
        var op = string.IsNullOrWhiteSpace(operation) ? "Handle" : operation.Trim();
        var success = exception == null && result is { IsSuccess: true };
        var output = result?.Output;
        var outputText = output switch
        {
            null => "",
            string s => s,
            _ => output.ToString() ?? ""
        };
        var preview = Truncate(outputText, MaxOutputPreviewChars);

        return JsonSerializer.Serialize(
            new
            {
                kind = "agctor.tool.invoke",
                toolId = toolId?.Trim() ?? "",
                operation = op,
                invokingAgentId = string.IsNullOrWhiteSpace(invokingAgentId) ? null : invokingAgentId.Trim(),
                parameters = SummarizeParameters(parameters),
                success,
                error = exception?.Message ?? (success ? null : result?.Error),
                outputChars = outputText.Length,
                outputPreview = string.IsNullOrEmpty(preview) ? null : preview,
                outputTruncated = outputText.Length > preview.Length,
                outputType = output?.GetType().Name
            },
            Json);
    }

    public static string FormatDisplayName(string toolId, string operation)
    {
        var name = FriendlyToolLabel(toolId);
        var op = string.IsNullOrWhiteSpace(operation) ? "Handle" : operation.Trim();
        return $"Tool · {name} · {op}";
    }

    public static string FriendlyToolLabel(string toolId)
    {
        if (string.IsNullOrWhiteSpace(toolId))
            return "unknown";
        var t = toolId.Trim();
        if (t.EndsWith("Tool", StringComparison.OrdinalIgnoreCase) && t.Length > 4)
            t = t[..^4];
        return t;
    }

    private static IReadOnlyList<object>? SummarizeParameters(IDictionary<string, object>? parameters)
    {
        if (parameters == null || parameters.Count == 0)
            return null;

        var list = new List<object>();
        foreach (var (key, value) in parameters.Take(MaxParams))
        {
            list.Add(new
            {
                key,
                value = TruncateParam(value),
                truncated = IsTruncated(value)
            });
        }

        return list;
    }

    private static bool IsTruncated(object? value)
    {
        if (value == null)
            return false;
        var s = value.ToString() ?? "";
        return s.Length > MaxParamValueChars;
    }

    private static string TruncateParam(object? value)
    {
        if (value == null)
            return "null";
        var s = value switch
        {
            string str => str,
            _ => value.ToString() ?? ""
        };
        return Truncate(s, MaxParamValueChars);
    }

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max)
            return s;
        return s[..max] + "…";
    }
}
