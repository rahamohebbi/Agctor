using AgctorSDK.Core.Rag;
using AgctorSDK.Core.Rag.Transport;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Extensions.Rag.Providers;

/// <summary>Cognee MCP HTTP adapter — search + cognify tools (PRD-025 Phase 2).</summary>
public sealed class CogneeProviderAdapter : IRagProviderAdapter, IRagCollectionCatalog
{
    private readonly IOptionsMonitor<RagOptions> _options;
    private readonly IMcpHttpRagTransport _mcp;

    public CogneeProviderAdapter(IOptionsMonitor<RagOptions> options, IMcpHttpRagTransport mcp)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _mcp = mcp ?? throw new ArgumentNullException(nameof(mcp));
    }

    private CogneeProviderOptions Cognee => _options.CurrentValue.Cognee;

    /// <inheritdoc />
    public string ProviderId => RagProviderIds.Cognee;

    /// <inheritdoc />
    public async Task<RagHealthResult> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        if (!TryBuildEndpoint(out var endpoint, out var error))
            return new RagHealthResult(RagHealthStatus.NotConfigured, error!);

        var list = await _mcp.SendAsync(endpoint, "tools/list", new { }, cancellationToken).ConfigureAwait(false);
        if (list.Success && (list.RawJson.Contains("recall", StringComparison.OrdinalIgnoreCase)
                             || list.RawJson.Contains("search", StringComparison.OrdinalIgnoreCase)))
            return new RagHealthResult(RagHealthStatus.Healthy, "Cognee MCP is reachable (tools/list ok).");

        if (list.Success)
            return new RagHealthResult(RagHealthStatus.Degraded, "Cognee MCP responded but recall/search tool was not listed.");

        return new RagHealthResult(RagHealthStatus.Unavailable, list.ErrorMessage ?? "Cognee MCP unreachable.");
    }

    /// <inheritdoc />
    public async Task<RagQueryResult> QueryAsync(RagQueryRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return new RagQueryResult(Array.Empty<RagContextChunk>());

        if (!TryBuildEndpoint(out var endpoint, out _))
            throw new InvalidOperationException("Cognee BaseUrl is not configured.");

        var searchType = CogneeMcpMapper.ResolveSearchType(request.Mode, Cognee.SearchType);
        var topK = Math.Clamp(request.TopK <= 0 ? 8 : request.TopK, 1, 100);
        var args = new Dictionary<string, object?>
        {
            ["query"] = request.Query.Trim(),
            ["top_k"] = topK
        };

        if (!string.IsNullOrWhiteSpace(searchType))
            args["search_type"] = searchType;

        if (!string.IsNullOrWhiteSpace(request.CollectionId))
            args["datasets"] = request.CollectionId.Trim();

        var result = await _mcp.InvokeToolAsync(endpoint, "recall", args, cancellationToken).ConfigureAwait(false);
        return CogneeMcpMapper.ParseSearch(result, request.Mode);
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

        if (!TryBuildEndpoint(out var endpoint, out var error))
            return new RagIngestResult(false, error!);

        var args = new Dictionary<string, object?> { ["data"] = text.Trim() };
        if (!string.IsNullOrWhiteSpace(request.CollectionId))
            args["dataset_name"] = request.CollectionId.Trim();
        else if (!string.IsNullOrWhiteSpace(request.SourcePath))
            args["dataset_name"] = Path.GetFileNameWithoutExtension(request.SourcePath);

        var result = await _mcp.InvokeToolAsync(endpoint, "remember", args, cancellationToken).ConfigureAwait(false);
        return CogneeMcpMapper.ParseCognify(result);
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> ListCollectionIdsAsync(CancellationToken cancellationToken = default)
    {
        if (!TryBuildEndpoint(out var endpoint, out _))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var result = await _mcp.InvokeToolAsync(
            endpoint,
            "list_datasets_json",
            new Dictionary<string, object?>(),
            cancellationToken).ConfigureAwait(false);

        return CogneeMcpMapper.ParseDatasetNames(result);
    }

    private bool TryBuildEndpoint(out string endpoint, out string? error)
    {
        error = null;
        endpoint = "";
        var baseUrl = Cognee.BaseUrl?.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            error = "Cognee BaseUrl is not configured.";
            return false;
        }

        var path = string.IsNullOrWhiteSpace(Cognee.McpPath) ? "/mcp" : Cognee.McpPath.Trim();
        if (!path.StartsWith('/'))
            path = "/" + path;

        endpoint = $"{baseUrl.TrimEnd('/')}{path}";
        return true;
    }
}
