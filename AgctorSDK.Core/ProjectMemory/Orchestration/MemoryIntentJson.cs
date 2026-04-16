using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Tools;

namespace AgctorSDK.Core.ProjectMemory.Orchestration;

/// <summary>
/// Parses extractor LLM output into <see cref="MemoryIntentBatch"/>; strips common markdown fences.
/// </summary>
public static class MemoryIntentJson
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    static MemoryIntentJson()
    {
        // LLM payloads often send number/bool/null for fields modeled as strings; coerce to text.
        JsonOptions.Converters.Add(new LenientStringJsonConverter());
    }

    /// <summary>Remove leading ```json / ``` wrapper if present.</summary>
    public static string UnwrapMarkdownFences(string text)
    {
        var t = (text ?? "").Trim();
        if (t.Length < 3 || !t.StartsWith("```", StringComparison.Ordinal))
            return t;

        var firstNl = t.IndexOf('\n');
        if (firstNl < 0)
            return t;
        var rest = t[(firstNl + 1)..];
        var end = rest.LastIndexOf("```", StringComparison.Ordinal);
        return end > 0 ? rest[..end].Trim() : rest.Trim();
    }

    /// <summary>Try deserialize <see cref="MemoryIntentBatch"/> after unwrapping fences.</summary>
    public static bool TryParseBatch(string rawLlmText, out MemoryIntentBatch? batch, out string? error)
    {
        batch = null;
        error = null;
        var json = UnwrapMarkdownFences(rawLlmText);
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "Empty extractor output.";
            return false;
        }

        foreach (var candidate in EnumerateJsonCandidates(json))
        {
            if (TryDeserializeBatch(candidate, out batch, out error))
            {
                NormalizeEntityKeys(batch!);
                return true;
            }
        }

        error ??= "Could not parse memoryIntents JSON.";
        return false;
    }

    /// <summary>Maps path-like extractor keys to folder slugs (e.g. <c>match/people/melody/</c> → <c>melody</c>).</summary>
    private static void NormalizeEntityKeys(MemoryIntentBatch batch)
    {
        foreach (var intent in batch.MemoryIntents)
        {
            var slug = EntityFolderBootstrapper.SlugFolderSegment(intent.EntityKey);
            if (slug.Length > 0)
                intent.EntityKey = slug;
        }
    }

    /// <summary>Models often prefix/suffix prose; pull the outermost <c>{"memoryIntents":…}</c> object.</summary>
    private static IEnumerable<string> EnumerateJsonCandidates(string text)
    {
        var t = text.Trim();
        yield return t;
        var embedded = TryExtractObjectContainingKey(t, "memoryIntents");
        if (!string.IsNullOrWhiteSpace(embedded) && !string.Equals(embedded, t, StringComparison.Ordinal))
            yield return embedded;
        var embeddedIntents = TryExtractObjectContainingKey(t, "intents");
        if (!string.IsNullOrWhiteSpace(embeddedIntents) && !string.Equals(embeddedIntents, t, StringComparison.Ordinal))
            yield return embeddedIntents;
        foreach (var fence in EnumerateInlineJsonFences(t))
            yield return fence;
    }

    private static IEnumerable<string> EnumerateInlineJsonFences(string text)
    {
        const StringComparison ord = StringComparison.Ordinal;
        var i = 0;
        while (i < text.Length)
        {
            var open = text.IndexOf("```", i, ord);
            if (open < 0)
                yield break;
            var afterTick = open + 3;
            var langEnd = text.IndexOf('\n', afterTick);
            if (langEnd < 0)
                yield break;
            var close = text.IndexOf("```", langEnd + 1, ord);
            if (close < 0)
                yield break;
            var inner = text.AsSpan(langEnd + 1, close - langEnd - 1).Trim();
            // Object envelope or bare array of intents (models often fence either shape).
            if (inner.Length > 0 && (inner.StartsWith("{", StringComparison.Ordinal) || inner.StartsWith("[", StringComparison.Ordinal)))
                yield return inner.ToString();
            i = close + 3;
        }
    }

    /// <summary>First balanced <c>{ … }</c> that contains <paramref name="key"/> as a JSON property name.</summary>
    public static string? TryExtractObjectContainingKey(string text, string key)
    {
        var needle = "\"" + key + "\"";
        var keyIdx = text.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        if (keyIdx < 0)
            return null;
        var start = text.LastIndexOf('{', keyIdx);
        if (start < 0)
            return null;
        var depth = 0;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '{')
                depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                    return text.Substring(start, i - start + 1);
            }
        }

        return null;
    }

    private static bool TryDeserializeBatch(string json, out MemoryIntentBatch? batch, out string? error)
    {
        batch = null;
        error = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                var list = JsonSerializer.Deserialize<List<MemoryIntent>>(root.GetRawText(), JsonOptions);
                if (list == null)
                {
                    error = "Intent array deserialized to null.";
                    return false;
                }

                batch = new MemoryIntentBatch { MemoryIntents = list };
                return true;
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "Root JSON must be an object with memoryIntents or a bare array of intents.";
                return false;
            }

            if (TryGetPropertyIgnoreCase(root, "memoryIntents", out var mi) && mi.ValueKind == JsonValueKind.Array)
            {
                batch = JsonSerializer.Deserialize<MemoryIntentBatch>(json, JsonOptions);
            }
            else if (TryGetPropertyIgnoreCase(root, "intents", out var alt) && alt.ValueKind == JsonValueKind.Array)
            {
                var list = JsonSerializer.Deserialize<List<MemoryIntent>>(alt.GetRawText(), JsonOptions);
                batch = new MemoryIntentBatch
                {
                    MemoryIntents = list ?? new List<MemoryIntent>(),
                    ScenarioId = TryGetStringPropertyIgnoreCase(root, "scenarioId")
                };
            }
            else
            {
                batch = JsonSerializer.Deserialize<MemoryIntentBatch>(json, JsonOptions);
            }

            if (batch?.MemoryIntents == null)
            {
                error = "Missing memoryIntents array.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement obj, string name, out JsonElement value)
    {
        foreach (var p in obj.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = p.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? TryGetStringPropertyIgnoreCase(JsonElement obj, string name)
    {
        return TryGetPropertyIgnoreCase(obj, name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
    }

    /// <summary>Coerces non-string JSON scalars into string values for robust extractor ingest.</summary>
    private sealed class LenientStringJsonConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString() ?? "",
                JsonTokenType.Number => reader.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture),
                JsonTokenType.True => bool.TrueString.ToLowerInvariant(),
                JsonTokenType.False => bool.FalseString.ToLowerInvariant(),
                JsonTokenType.Null => "",
                _ => JsonDocument.ParseValue(ref reader).RootElement.ToString()
            };
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value);
    }
}
