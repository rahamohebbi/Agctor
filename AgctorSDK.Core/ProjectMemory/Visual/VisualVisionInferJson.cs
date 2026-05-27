using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgctorSDK.Core.ProjectMemory.Visual;

/// <summary>JSON shape for <see cref="VisualExtractPrompts.InferVersion"/> responses.</summary>
public sealed class VisualVisionInferPayload
{
    [JsonPropertyName("entityKeys")]
    public List<string> EntityKeys { get; set; } = new();

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("rationale")]
    public string? Rationale { get; set; }

    [JsonPropertyName("suggestedIntent")]
    public string? SuggestedIntent { get; set; }

    public static bool TryParse(string rawJson, out VisualVisionInferPayload? payload, out string? error)
    {
        payload = null;
        error = null;
        var json = AgctorSDK.Core.Ollama.OllamaThinkBlockStripper.Strip(rawJson);
        json = AgctorSDK.Core.ProjectMemory.Orchestration.MemoryIntentJson.UnwrapMarkdownFences(json);
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "Empty infer JSON.";
            return false;
        }

        try
        {
            payload = JsonSerializer.Deserialize<VisualVisionInferPayload>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (payload == null)
            {
                error = "Infer JSON deserialized to null.";
                return false;
            }

            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
