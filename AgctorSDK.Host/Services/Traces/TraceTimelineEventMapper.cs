using System.Collections.Generic;
using System.Text.Json;
using AgctorSDK.Core.Tools.Models;
using AgctorSDK.Core.Utils.ActivityTracking;
using AgctorSDK.Core.Utils.ActivityTracking.Logger;
using AgctorSDK.Core.Utils.Observability.Visualization;
using AgctorSDK.Host.Models;

namespace AgctorSDK.Host.Services.Traces;

/// <summary>Maps <see cref="IActivity"/> records to timeline DTOs with UI-friendly metadata.</summary>
public static class TraceTimelineEventMapper
{
    public static TraceTimelineEventDto Map(IActivity activity, int sequence, DateTimeOffset traceStart, IReadOnlyDictionary<string, int> depthMap)
    {
        var kind = ClassifyKind(activity);
        return new TraceTimelineEventDto
        {
            Id = activity.Id,
            ParentId = activity.ParentId,
            Label = activity.DisplayName ?? activity.Name ?? "Activity",
            Name = activity.Name,
            Sequence = sequence,
            Depth = depthMap.TryGetValue(activity.Id, out var depth) ? depth : 0,
            StartedAtUtc = activity.Timestamp,
            StartOffsetMs = Math.Max(0, (activity.Timestamp - traceStart).TotalMilliseconds),
            DurationMs = Math.Max(1, activity.Duration.TotalMilliseconds),
            HasResult = activity.HasResult,
            TimelineDetailJson = activity.TimelineDetailJson,
            EventKind = kind,
            Status = ClassifyStatus(activity, kind)
        };
    }

    public static string ClassifyKind(IActivity activity)
    {
        var fromJson = TryParseKind(activity.TimelineDetailJson);
        if (!string.IsNullOrEmpty(fromJson))
            return MapDetailKind(fromJson);

        var name = activity.Name ?? "";
        if (name.StartsWith("tool.", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Tool.Handle", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Tool.ReceiveAsync", StringComparison.OrdinalIgnoreCase))
            return "tool";

        if (name.Contains("persona-llm", StringComparison.OrdinalIgnoreCase))
            return "llm";
        if (name.Contains("ingest", StringComparison.OrdinalIgnoreCase))
            return "ingest";
        if (name.Contains("persist", StringComparison.OrdinalIgnoreCase))
            return "persist";
        if (name.Contains("resolve", StringComparison.OrdinalIgnoreCase))
            return "resolve";
        if (name.StartsWith("http.", StringComparison.OrdinalIgnoreCase))
            return "http";
        return "other";
    }

    private static string MapDetailKind(string kind) => kind switch
    {
        "agctor.tool.invoke" => "tool",
        "pm.playground.persona-llm" => "llm",
        "pm.playground.ingest-disk" => "ingest",
        "pm.playground.persist-assistant" => "persist",
        "pm.playground.stream-root" => "http",
        "pm.playground.resolve" => "resolve",
        _ => "other"
    };

    private static string? TryParseKind(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String)
                return k.GetString();
        }
        catch
        {
            /* ignore */
        }

        return null;
    }

    private static string ClassifyStatus(IActivity activity, string kind)
    {
        if (activity is ActivityInfo info && info.Status == ActivityStatus.Error)
            return "error";

        if (kind == "tool" && !string.IsNullOrWhiteSpace(activity.TimelineDetailJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(activity.TimelineDetailJson);
                if (doc.RootElement.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.False)
                    return "error";
            }
            catch
            {
                /* ignore */
            }
        }

        return activity.HasResult || (activity is ActivityInfo ai && ai.Status == ActivityStatus.Completed)
            ? "ok"
            : "running";
    }
}
