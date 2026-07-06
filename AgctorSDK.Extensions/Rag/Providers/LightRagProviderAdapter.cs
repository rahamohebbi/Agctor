using AgctorSDK.Core.Rag;
using AgctorSDK.Core.Rag.Transport;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Extensions.Rag.Providers;

/// <summary>LightRAG REST adapter — /health, /query/data, /documents/text (PRD-025 Phase 2).</summary>
public sealed class LightRagProviderAdapter : IRagProviderAdapter
{
    private readonly IOptionsMonitor<RagOptions> _options;
    private readonly IRestRagTransport _rest;

    public LightRagProviderAdapter(IOptionsMonitor<RagOptions> options, IRestRagTransport rest)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _rest = rest ?? throw new ArgumentNullException(nameof(rest));
    }

    private LightRagProviderOptions LightRag => _options.CurrentValue.LightRAG;

    /// <inheritdoc />
    public string ProviderId => RagProviderIds.LightRag;

    /// <inheritdoc />
    public async Task<RagHealthResult> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        if (!TryBuildUrl("/health", out var url, out var error))
            return new RagHealthResult(RagHealthStatus.NotConfigured, error!);

        var response = await _rest.SendAsync(
            new RagRestCall(HttpMethod.Get, url, Headers: BuildHeaders()),
            cancellationToken).ConfigureAwait(false);
        return LightRagApiMapper.ParseHealth(response);
    }

    /// <inheritdoc />
    public async Task<RagQueryResult> QueryAsync(RagQueryRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return new RagQueryResult(Array.Empty<RagContextChunk>());

        if (!TryBuildUrl("/query/data", out var url, out var _))
            throw new InvalidOperationException("LightRAG BaseUrl is not configured.");

        var mode = LightRagApiMapper.ToLightRagMode(request.Mode, LightRag.DefaultMode);
        var topK = Math.Clamp(request.TopK <= 0 ? 8 : request.TopK, 1, 100);
        var body = System.Text.Json.JsonSerializer.Serialize(new
        {
            query = request.Query.Trim(),
            mode,
            top_k = topK
        });

        var response = await _rest.SendAsync(
            new RagRestCall(HttpMethod.Post, url, body, BuildHeaders()),
            cancellationToken).ConfigureAwait(false);

        return LightRagApiMapper.ParseQueryData(response);
    }

    /// <inheritdoc />
    public async Task<RagIngestResult> IngestAsync(RagIngestRequest request, CancellationToken cancellationToken = default)
    {
        var text = request.Content;
        if (string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(request.SourcePath)
                                             && File.Exists(request.SourcePath))
            text = await File.ReadAllTextAsync(request.SourcePath, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(text))
            return new RagIngestResult(false, "Ingest requires Content or an existing SourcePath file.");

        if (!TryBuildUrl("/documents/text", out var url, out var error))
            return new RagIngestResult(false, error!);

        var source = LightRagApiMapper.ToUniqueFileSource(
            request.SourcePath ?? request.CollectionId ?? $"agctor-{Guid.NewGuid():N}.md");
        var body = System.Text.Json.JsonSerializer.Serialize(new
        {
            text = text.Trim(),
            file_source = source
        });

        var response = await _rest.SendAsync(
            new RagRestCall(HttpMethod.Post, url, body, BuildHeaders()),
            cancellationToken).ConfigureAwait(false);

        return LightRagApiMapper.ParseIngest(response);
    }

    private bool TryBuildUrl(string path, out string url, out string? error)
    {
        error = null;
        url = "";
        var baseUrl = LightRag.BaseUrl?.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            error = "LightRAG BaseUrl is not configured.";
            return false;
        }

        url = $"{baseUrl.TrimEnd('/')}{path}";
        return true;
    }

    private IReadOnlyDictionary<string, string>? BuildHeaders()
    {
        if (string.IsNullOrWhiteSpace(LightRag.ApiKey))
            return null;

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["X-API-Key"] = LightRag.ApiKey.Trim()
        };
    }
}
