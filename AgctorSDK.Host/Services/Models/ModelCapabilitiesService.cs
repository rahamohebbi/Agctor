using System.Net.Http.Json;
using System.Text.Json;
using AgctorSDK.Core.Ollama;

namespace AgctorSDK.Host.Services.Models;

public sealed record SelectableModel(string Name, bool Completion, bool Decision);

public sealed class ModelCapabilitiesService(IHttpClientFactory clients, IOllamaModelCatalog catalog)
{
    public async Task<IReadOnlyList<SelectableModel>> ListAsync(CancellationToken ct)
    {
        var models = await catalog.ListLocalModelsAsync(ct);
        var result = new System.Collections.Concurrent.ConcurrentBag<SelectableModel>();
        await Parallel.ForEachAsync(models, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, async (model, token) =>
        {
            result.Add(await InspectAsync(model.Name, token));
        });
        return result.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private async Task<SelectableModel> InspectAsync(string model, CancellationToken ct)
    {
        using var http = clients.CreateClient(OllamaDecisionClient.HttpClientName);
        using var response = await http.PostAsJsonAsync(OllamaRuntimeConfiguration.GetApiUrlWithTrailingSlash() + "api/show", new { model }, ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Model '{model}' is unavailable in Ollama. Install it or choose another model.");
        var info = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var capabilities = info.TryGetProperty("capabilities", out var caps) && caps.ValueKind == JsonValueKind.Array
            ? caps.EnumerateArray().Select(c => c.GetString()).ToArray() : [];
        return new(model, capabilities.Contains("completion"), capabilities.Contains("decision"));
    }
    public async Task ValidateAsync(IEnumerable<ResolvedModel> selections, CancellationToken ct)
    {
        foreach (var group in selections.GroupBy(s => s.Model, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(group.Key)) throw new InvalidOperationException("No decision model is configured. Choose one in the scenario, router, or chat model settings.");
            var model = await InspectAsync(group.Key, ct);
            foreach (var selection in group)
                if (selection.Role == ModelRoles.DecisionRouting ? !model.Decision : !model.Completion)
                    throw new InvalidOperationException($"Model '{model.Name}' does not support {selection.Role}. Choose a compatible model.");
        }
    }
}
