using System.Text.Json;

namespace AgctorSDK.Core.Streaming
{
    /// <summary>
    /// Parses one NDJSON line from Ollama <c>/api/generate</c> when <c>stream: true</c>.
    /// </summary>
    public static class OllamaStreamLineParser
    {
        public static bool TryParseLine(string line, out string? token, out bool done)
        {
            token = null;
            done = false;
            line = line.Trim();
            if (line.Length == 0)
            {
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.TryGetProperty("response", out var r) && r.ValueKind == JsonValueKind.String)
                {
                    token = r.GetString();
                }

                if (root.TryGetProperty("done", out var d) && d.ValueKind == JsonValueKind.True)
                {
                    done = true;
                }
                else if (root.TryGetProperty("done", out var d2) && d2.ValueKind == JsonValueKind.False)
                {
                    done = false;
                }

                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
