using System.Text.Json.Serialization;

namespace AgctorSDK.Core.IntegrationTests.Agents
{
    public class OllamaGenerateRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = default!;

        [JsonPropertyName("prompt")]
        public string Prompt { get; set; } = default!;

        [JsonPropertyName("stream")]
        public bool Stream { get; set; } = false;
    }

    public class OllamaGenerateResponse
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = default!;

        [JsonPropertyName("created_at")]
        public System.DateTime CreatedAt { get; set; }

        [JsonPropertyName("response")]
        public string Response { get; set; } = default!;

        [JsonPropertyName("done")]
        public bool Done { get; set; }
    }
} 