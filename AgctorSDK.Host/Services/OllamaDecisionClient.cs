using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using AgctorSDK.Core.Ollama;

namespace AgctorSDK.Host.Services;

public interface IOllamaDecisionClient
{
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken);
    Task<JsonElement> DecideAsync(string model, string state, IReadOnlyDictionary<string, object> questions,
        CancellationToken cancellationToken);
}

/// <summary>Native System One transport, independent of the chat/completion model.</summary>
public sealed class OllamaDecisionClient(IHttpClientFactory clients) : IOllamaDecisionClient
{
    public const string HttpClientName = "OllamaDecision";

    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken)
    {
        using var http = clients.CreateClient(HttpClientName);
        var baseUrl = OllamaRuntimeConfiguration.GetApiUrlWithTrailingSlash();
        var tags = await http.GetFromJsonAsync<JsonElement>(baseUrl + "api/tags", cancellationToken);
        var names = tags.GetProperty("models").EnumerateArray()
            .Select(m => m.GetProperty("name").GetString()!)
            .Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.Ordinal).ToArray();
        var decisions = new ConcurrentBag<string>();
        await Parallel.ForEachAsync(names, new ParallelOptions
        {
            MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken
        }, async (name, ct) =>
        {
            using var response = await http.PostAsJsonAsync(baseUrl + "api/show", new { model = name }, ct);
            response.EnsureSuccessStatusCode();
            var info = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            if (info.TryGetProperty("capabilities", out var caps) && caps.ValueKind == JsonValueKind.Array
                && caps.EnumerateArray().Any(c => c.ValueKind == JsonValueKind.String && c.GetString() == "decision"))
                decisions.Add(name);
        });
        return decisions.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<JsonElement> DecideAsync(string model, string state,
        IReadOnlyDictionary<string, object> questions, CancellationToken cancellationToken)
    {
        using var http = clients.CreateClient(HttpClientName);
        using var response = await http.PostAsJsonAsync(
            OllamaRuntimeConfiguration.GetApiUrlWithTrailingSlash() + "v1/systemone",
            new { model, state, questions }, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // Do not expose the server response body: it may echo user state.
            throw new HttpRequestException(
                $"Decision model '{model}' failed (HTTP {(int)response.StatusCode}). " +
                "Check that the model is installed, supports decisions, and Ollama supports /v1/systemone.");
        }
        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
    }
}
