using System;
using System.Text.Json.Serialization;

namespace AgctorSDK.Core.Ollama;

/// <summary>JSON body for Ollama <c>POST /api/generate</c> (shared by LLMAgent and project-memory).</summary>
public sealed class OllamaGenerateRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = default!;

    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = default!;

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }
}

/// <summary>Non-streaming JSON response from Ollama <c>/api/generate</c>.</summary>
public sealed class OllamaGenerateResponse
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = default!;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("response")]
    public string? Response { get; set; }

    [JsonPropertyName("done")]
    public bool Done { get; set; }
}
