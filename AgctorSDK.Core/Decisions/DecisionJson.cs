using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgctorSDK.Core.Decisions
{
    /// <summary>
    /// JSON settings and the small reader that turns a provider payload into a <see cref="DecisionResult"/>.
    /// Kept in one place so Laya and the LLM adapter cannot drift apart on confidence, scores, or summaries.
    /// </summary>
    internal static class DecisionJson
    {
        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public static DecisionResult Read(DecisionRequest request, string provider, JsonElement json, decimal? defaultCost)
        {
            var confidence = ReadDouble(json, "confidence") ?? 0d;
            var summary = ReadString(json, "summary") ?? ReadString(json, "reasoningSummary");
            var cost = ReadDecimal(json, "cost") ?? defaultCost;

            object? value = request.Type switch
            {
                DecisionType.Score => (object?)(ReadDouble(json, "score") ?? ReadDouble(json, "value") ?? 0d),
                DecisionType.Binary => ReadBool(json, "value") ?? ReadBool(json, "selected") ?? false,
                _ => ReadString(json, "selected") ?? ReadString(json, "value") ?? string.Empty
            };

            return new DecisionResult
            {
                DecisionId = request.Id,
                Provider = provider,
                Value = value,
                Confidence = confidence,
                Scores = ReadScores(json),
                ReasoningSummary = summary,
                Cost = cost
            };
        }

        private static Dictionary<string, double>? ReadScores(JsonElement json)
        {
            if (!json.TryGetProperty("scores", out var scores) || scores.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in scores.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetDouble(out var number))
                {
                    map[property.Name] = number;
                }
            }

            return map.Count == 0 ? null : map;
        }

        private static string? ReadString(JsonElement json, string name)
        {
            if (!json.TryGetProperty(name, out var value))
            {
                return null;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            };
        }

        private static double? ReadDouble(JsonElement json, string name)
        {
            if (!json.TryGetProperty(name, out var value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
            {
                return number;
            }

            if (value.ValueKind == JsonValueKind.String &&
                double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            return null;
        }

        private static bool? ReadBool(JsonElement json, string name)
        {
            if (!json.TryGetProperty(name, out var value))
            {
                return null;
            }

            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(value.GetString(), out var flag) => flag,
                _ => null
            };
        }

        private static decimal? ReadDecimal(JsonElement json, string name)
        {
            if (!json.TryGetProperty(name, out var value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
            {
                return number;
            }

            return null;
        }
    }
}
