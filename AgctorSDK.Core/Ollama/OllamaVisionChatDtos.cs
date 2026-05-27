using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AgctorSDK.Core.Ollama;

public sealed class OllamaVisionChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("messages")]
    public List<OllamaVisionChatMessage> Messages { get; set; } = new();

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }

    /// <summary>Disable reasoning trace so JSON lands in <see cref="OllamaVisionChatMessage.Content"/> (Gemma 4).</summary>
    [JsonPropertyName("think")]
    public bool Think { get; set; }

    [JsonPropertyName("options")]
    public OllamaVisionChatOptions? Options { get; set; }
}

public sealed class OllamaVisionChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "user";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    [JsonPropertyName("images")]
    public List<string>? Images { get; set; }

    [JsonPropertyName("thinking")]
    public string? Thinking { get; set; }
}

public sealed class OllamaVisionChatOptions
{
    [JsonPropertyName("num_predict")]
    public int? NumPredict { get; set; }
}

public sealed class OllamaVisionChatResponse
{
    [JsonPropertyName("message")]
    public OllamaVisionChatMessage? Message { get; set; }

    [JsonPropertyName("done")]
    public bool Done { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public sealed class OllamaVisionChatResult
{
    public required bool Success { get; init; }

    public string Content { get; init; } = "";

    public string ModelUsed { get; init; } = "";

    public string? Error { get; init; }
}
