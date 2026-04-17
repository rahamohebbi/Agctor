using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AgctorSDK.Core.Agents;

namespace AgctorSDK.Host.Services;

/// <summary>
/// Calls Ollama <c>GET /api/tags</c> using the same base URL as <see cref="LLMAgent"/> runtime defaults.
/// </summary>
public sealed class OllamaModelCatalog : IOllamaModelCatalog
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    public async Task<IReadOnlyList<OllamaModelListItem>> ListLocalModelsAsync(CancellationToken cancellationToken = default)
    {
        var baseUrl = LLMAgent.GetConfiguredOllamaApiUrl().TrimEnd('/');
        var resp = await Http.GetAsync($"{baseUrl}/api/tags", cancellationToken).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var payload = await resp.Content.ReadFromJsonAsync<OllamaTagsResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (payload?.Models == null)
            return Array.Empty<OllamaModelListItem>();

        return payload.Models
            .Where(m => !string.IsNullOrWhiteSpace(m.Name))
            .Select(m => new OllamaModelListItem
            {
                Name = m.Name!.Trim(),
                Size = m.Size,
                ModifiedAt = m.ModifiedAt
            })
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private sealed class OllamaTagsResponse
    {
        [JsonPropertyName("models")]
        public List<OllamaTagModel>? Models { get; set; }
    }

    private sealed class OllamaTagModel
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("size")]
        public long? Size { get; set; }

        [JsonPropertyName("modified_at")]
        public string? ModifiedAt { get; set; }
    }
}
