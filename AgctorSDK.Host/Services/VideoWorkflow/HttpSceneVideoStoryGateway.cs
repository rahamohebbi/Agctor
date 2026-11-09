using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Host.Services.VideoWorkflow;

public sealed class HttpSceneVideoStoryGateway(IHttpClientFactory clients, IOptionsMonitor<SceneVideoOptions> options) : ISceneVideoStoryGateway
{
    public Task<SceneVideoStory> EnsureAsync(string key, string name, CancellationToken ct) =>
        SendAsync<SceneVideoStory>(HttpMethod.Put, "/stories/" + Uri.EscapeDataString(key), new { name }, key, ct);
    public async Task<IReadOnlyList<SceneVideoStory>> ListAsync(CancellationToken ct) =>
        await SendAsync<SceneVideoStory[]>(HttpMethod.Get, "/stories", null, null, ct);
    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, string? key, CancellationToken ct)
    {
        var address = options.CurrentValue.RendererBaseUrl;
        if (!Uri.TryCreate(address, UriKind.Absolute, out var root) || root.Scheme is not ("http" or "https"))
            throw new SceneRenderInterventionException("Connect Cynelo before creating a story, then retry this step.");
        using var request = new HttpRequestMessage(method, address!.TrimEnd('/') + path);
        if (body != null) request.Content = JsonContent.Create(body);
        if (key != null) request.Headers.Add("Idempotency-Key", key);
        using var http = clients.CreateClient("SceneVideoRenderer");
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            throw new SceneRenderInterventionException("Cynelo story setup is unavailable. Check its connection and update Cynelo's Agctor integration, then retry this step.");
        if (response.StatusCode == HttpStatusCode.Conflict)
            throw new SceneRenderInterventionException("This story request conflicts with an earlier request. Start a new sequence with a new request ID.");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Cynelo returned no story information.");
    }
}
