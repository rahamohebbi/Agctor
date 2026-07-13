using AgctorSDK.Core.Rag;
using AgctorSDK.Core.Rag.Transport;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Extensions.Rag.Providers;

/// <summary>Graphiti REST adapter — /healthcheck, /search, /messages (temporal graph RAG).</summary>
public sealed class GraphitiProviderAdapter : IRagProviderAdapter
{
    private readonly IOptionsMonitor<RagOptions> _options;
    private readonly IRestRagTransport _rest;

    public GraphitiProviderAdapter(IOptionsMonitor<RagOptions> options, IRestRagTransport rest)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _rest = rest ?? throw new ArgumentNullException(nameof(rest));
    }

    private GraphitiProviderOptions Graphiti => _options.CurrentValue.Graphiti;

    /// <inheritdoc />
    public string ProviderId => RagProviderIds.Graphiti;

    /// <inheritdoc />
    public async Task<RagHealthResult> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        if (!TryBuildUrl("/healthcheck", out var url, out var error))
            return new RagHealthResult(RagHealthStatus.NotConfigured, error!);

        var response = await _rest.SendAsync(
            new RagRestCall(HttpMethod.Get, url, Headers: BuildHeaders()),
            cancellationToken).ConfigureAwait(false);
        return GraphitiApiMapper.ParseHealth(response);
    }

    /// <inheritdoc />
    public async Task<RagQueryResult> QueryAsync(RagQueryRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return new RagQueryResult(Array.Empty<RagContextChunk>());

        if (!TryBuildUrl("/search", out var url, out var _))
            throw new InvalidOperationException("Graphiti BaseUrl is not configured.");

        var groupId = GraphitiApiMapper.ResolveGroupId(request.CollectionId, Graphiti.DefaultGroupId);
        var topK = Math.Clamp(request.TopK <= 0 ? 8 : request.TopK, 1, 100);
        var body = System.Text.Json.JsonSerializer.Serialize(new
        {
            query = request.Query.Trim(),
            group_ids = new[] { groupId },
            max_facts = topK
        });

        var response = await _rest.SendAsync(
            new RagRestCall(HttpMethod.Post, url, body, BuildHeaders()),
            cancellationToken).ConfigureAwait(false);

        return GraphitiApiMapper.ParseSearch(response);
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

        if (!TryBuildUrl("/messages", out var url, out var error))
            return new RagIngestResult(false, error!);

        var groupId = GraphitiApiMapper.ResolveGroupId(request.CollectionId, Graphiti.DefaultGroupId);
        var episodeName = GraphitiApiMapper.ToEpisodeName(
            request.SourcePath ?? request.CollectionId);
        var body = System.Text.Json.JsonSerializer.Serialize(new
        {
            group_id = groupId,
            messages = new[]
            {
                new
                {
                    content = text.Trim(),
                    role_type = "user",
                    role = "agctor",
                    name = episodeName,
                    source_description = request.SourcePath ?? episodeName,
                    timestamp = DateTime.UtcNow.ToString("O")
                }
            }
        });

        var response = await _rest.SendAsync(
            new RagRestCall(HttpMethod.Post, url, body, BuildHeaders()),
            cancellationToken).ConfigureAwait(false);

        return GraphitiApiMapper.ParseIngest(response);
    }

    private bool TryBuildUrl(string path, out string url, out string? error)
    {
        error = null;
        url = "";
        var baseUrl = Graphiti.BaseUrl?.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            error = "Graphiti BaseUrl is not configured.";
            return false;
        }

        url = $"{baseUrl.TrimEnd('/')}{path}";
        return true;
    }

    private IReadOnlyDictionary<string, string>? BuildHeaders()
    {
        if (string.IsNullOrWhiteSpace(Graphiti.ApiKey))
            return null;

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["X-API-Key"] = Graphiti.ApiKey.Trim()
        };
    }
}
