using AgctorSDK.Core.Rag;
using AgctorSDK.Core.Rag.Ingest;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Host.Models;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Host.Services;

/// <summary>
/// Shared read/write logic for the RAG providers dashboard (API + Razor components).
/// </summary>
public interface IRagProvidersDashboardService
{
    Task<RagProviderStatusResponseDto> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<UpdateRagProviderSelectionResponseDto> SaveSelectionAsync(
        UpdateRagProviderSelectionDto body,
        CancellationToken cancellationToken = default);
    Task<RagProviderHealthResponseDto> GetHealthAsync(CancellationToken cancellationToken = default);
    Task<RagProviderQueryResponseDto> QueryAsync(
        RagProviderQueryRequestDto body,
        CancellationToken cancellationToken = default);
    Task<RagIngestSourcesResponseDto> GetIngestSourcesAsync(CancellationToken cancellationToken = default);
    Task<RagProviderIngestPreviewResponseDto> PreviewIngestAsync(
        RagProviderIngestRequestDto body,
        CancellationToken cancellationToken = default);
    Task<RagProviderIngestResponseDto> IngestAsync(
        RagProviderIngestRequestDto body,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class RagProvidersDashboardService : IRagProvidersDashboardService
{
    private readonly IRagProviderAdapterFactory _factory;
    private readonly IConfiguration _configuration;
    private readonly IUserRagSettingsService _userSettings;
    private readonly IRagProviderDockerService _docker;
    private readonly RagIngestOrchestrator _ingest;
    private readonly IOptionsMonitor<ProjectMemoryAgentOptions> _projectMemory;
    private readonly ILogger<RagProvidersDashboardService> _logger;

    public RagProvidersDashboardService(
        IRagProviderAdapterFactory factory,
        IConfiguration configuration,
        IUserRagSettingsService userSettings,
        IRagProviderDockerService docker,
        RagIngestOrchestrator ingest,
        IOptionsMonitor<ProjectMemoryAgentOptions> projectMemory,
        ILogger<RagProvidersDashboardService> logger)
    {
        _factory = factory;
        _configuration = configuration;
        _userSettings = userSettings;
        _docker = docker;
        _ingest = ingest;
        _projectMemory = projectMemory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<RagProviderStatusResponseDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var configured = BuildConfiguredDto();
        var providerId = _factory.GetDefaultProviderId();
        var adapter = _factory.CreateDefaultProvider();
        var health = await adapter.GetHealthAsync(cancellationToken).ConfigureAwait(false);

        var available = RagProviderCatalog.All
            .Where(d => _factory.IsProviderAvailable(d.Id))
            .Select(BuildAvailableDto)
            .ToList();

        return new RagProviderStatusResponseDto
        {
            Current = new CurrentRagProviderDto
            {
                ProviderId = providerId,
                Transport = ResolveTransport(providerId, configured),
                HealthStatus = health.Status.ToString(),
                HealthMessage = health.Message
            },
            Configured = configured,
            Available = available
        };
    }

    /// <inheritdoc />
    public async Task<UpdateRagProviderSelectionResponseDto> SaveSelectionAsync(
        UpdateRagProviderSelectionDto body,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(body);

        var canonical = RagProviderIds.Normalize(body.DefaultProvider);
        if (!_factory.IsProviderAvailable(canonical))
            throw new InvalidOperationException($"Unknown RAG provider '{body.DefaultProvider}'.");

        var configured = BuildConfiguredDto();
        var light = body.LightRAG ?? configured.LightRAG;
        var cognee = body.Cognee ?? configured.Cognee;

        await _userSettings.PersistAsync(new RagSettingsUpdate
        {
            DefaultProvider = canonical,
            LightRAG = MapLightRagOptions(light),
            Cognee = MapCogneeOptions(cognee)
        }, cancellationToken).ConfigureAwait(false);

        if (_configuration is IConfigurationRoot configRoot)
            configRoot.Reload();

        return new UpdateRagProviderSelectionResponseDto
        {
            PersistedProviderId = canonical,
            Message = $"Saved {canonical} as default RAG provider."
        };
    }

    /// <inheritdoc />
    public async Task<RagProviderHealthResponseDto> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        var providerId = _factory.GetDefaultProviderId();
        var adapter = _factory.CreateDefaultProvider();
        var health = await adapter.GetHealthAsync(cancellationToken).ConfigureAwait(false);

        RagProviderDockerStatusDto? dockerDto = null;
        var overall = MapHealthOverall(health.Status);
        string? detail = health.Message;

        if (RagProviderConfigSchema.DockerBackedProviders.Contains(providerId))
        {
            var docker = await _docker.GetStatusAsync(providerId, cancellationToken).ConfigureAwait(false);
            dockerDto = MapDocker(docker);
            if (docker.State is not ("running" or "not_applicable"))
            {
                overall = "degraded";
                detail = docker.Message ?? "Docker sidecar is not running.";
            }
            else if (health.Status is RagHealthStatus.Unavailable or RagHealthStatus.NotConfigured)
            {
                overall = "degraded";
            }
        }
        else if (health.Status is RagHealthStatus.Unavailable or RagHealthStatus.NotConfigured)
        {
            overall = "degraded";
        }

        return new RagProviderHealthResponseDto
        {
            ProviderId = providerId,
            OverallStatus = overall,
            ProviderHealthStatus = health.Status.ToString(),
            Detail = detail,
            Docker = dockerDto
        };
    }

    /// <inheritdoc />
    public async Task<RagProviderQueryResponseDto> QueryAsync(
        RagProviderQueryRequestDto body,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (string.IsNullOrWhiteSpace(body.Query))
        {
            return new RagProviderQueryResponseDto
            {
                Success = false,
                ProviderId = _factory.GetDefaultProviderId(),
                Message = "Query text is required."
            };
        }

        var providerId = string.IsNullOrWhiteSpace(body.ProviderId)
            ? _factory.GetDefaultProviderId()
            : RagProviderIds.Normalize(body.ProviderId);

        try
        {
            var adapter = _factory.CreateProvider(providerId);
            var mode = ResolveDashboardQueryMode(providerId, body.Mode);
            var result = await adapter.QueryAsync(new RagQueryRequest(
                body.Query.Trim(),
                body.CollectionId,
                body.TopK <= 0 ? 8 : body.TopK,
                Mode: mode), cancellationToken).ConfigureAwait(false);

            var chunkMessage = result.Chunks.Count == 0
                ? BuildEmptyQueryMessage(providerId, result)
                : $"Retrieved {result.Chunks.Count} chunk(s).";

            return new RagProviderQueryResponseDto
            {
                Success = true,
                ProviderId = providerId,
                Message = chunkMessage,
                Chunks = result.Chunks.Select(c => new RagContextChunkDto
                {
                    Text = c.Text,
                    Score = c.Score,
                    SourcePath = c.SourcePath
                }).ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RAG test query failed for {ProviderId}", providerId);
            return new RagProviderQueryResponseDto
            {
                Success = false,
                ProviderId = providerId,
                Message = ex.Message
            };
        }
    }

    /// <inheritdoc />
    public Task<RagIngestSourcesResponseDto> GetIngestSourcesAsync(CancellationToken cancellationToken = default)
    {
        var root = ResolveProjectRootOverride(null);
        var configured = !string.IsNullOrWhiteSpace(root)
                         && Directory.Exists(Path.Combine(Path.GetFullPath(root), ".agctor"));

        var sources = RagIngestSourceCatalog.All
            .Select(s => new RagIngestSourceDto
            {
                Id = s.Id,
                DisplayName = s.DisplayName,
                Description = s.Description,
                IsImplemented = s.IsImplemented
            })
            .ToList();

        return Task.FromResult(new RagIngestSourcesResponseDto
        {
            Sources = sources,
            ProjectRoot = root,
            ProjectRootConfigured = configured
        });
    }

    /// <inheritdoc />
    public async Task<RagProviderIngestPreviewResponseDto> PreviewIngestAsync(
        RagProviderIngestRequestDto body,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        var sourceId = RagIngestSourceIds.Normalize(body.SourceId);
        var context = BuildIngestContext(body);

        try
        {
            var preview = await _ingest.PreviewAsync(sourceId, context, cancellationToken).ConfigureAwait(false);
            var ok = preview.DocumentCount > 0
                     && !preview.Message.StartsWith("Project root", StringComparison.OrdinalIgnoreCase);
            return new RagProviderIngestPreviewResponseDto
            {
                Success = ok,
                SourceId = sourceId,
                DocumentCount = preview.DocumentCount,
                DatasetBatchCount = preview.DatasetBatchCount,
                SamplePaths = preview.SamplePaths,
                Message = preview.Message
            };
        }
        catch (InvalidOperationException ex)
        {
            return new RagProviderIngestPreviewResponseDto
            {
                Success = false,
                SourceId = sourceId,
                Message = ex.Message
            };
        }
    }

    /// <inheritdoc />
    public async Task<RagProviderIngestResponseDto> IngestAsync(
        RagProviderIngestRequestDto body,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        var sourceId = RagIngestSourceIds.Normalize(body.SourceId);
        var providerId = string.IsNullOrWhiteSpace(body.ProviderId)
            ? _factory.GetDefaultProviderId()
            : RagProviderIds.Normalize(body.ProviderId);
        var context = BuildIngestContext(body);

        try
        {
            var batch = await _ingest.IngestAsync(providerId, sourceId, context, cancellationToken)
                .ConfigureAwait(false);
            return MapIngestBatch(batch);
        }
        catch (InvalidOperationException ex)
        {
            return new RagProviderIngestResponseDto
            {
                Success = false,
                ProviderId = providerId,
                SourceId = sourceId,
                Message = ex.Message
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RAG ingest failed for {ProviderId} source {SourceId}", providerId, sourceId);
            return new RagProviderIngestResponseDto
            {
                Success = false,
                ProviderId = providerId,
                SourceId = sourceId,
                Message = ex.Message
            };
        }
    }

    private RagIngestSourceContext BuildIngestContext(RagProviderIngestRequestDto body)
    {
        var root = ResolveProjectRootOverride(body.ProjectRoot);
        IReadOnlyDictionary<string, string>? options = body.ForceReingest
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [RagIngestOptionKeys.ForceReingest] = "true"
            }
            : null;

        return new RagIngestSourceContext(root ?? "", body.CollectionId, options);
    }

    private string? ResolveProjectRootOverride(string? overrideRoot)
    {
        if (!string.IsNullOrWhiteSpace(overrideRoot))
            return Path.GetFullPath(overrideRoot.Trim());

        var configured = _projectMemory.CurrentValue.ProjectRoot?.Trim();
        return string.IsNullOrWhiteSpace(configured) ? null : Path.GetFullPath(configured);
    }

    private static RagProviderIngestResponseDto MapIngestBatch(RagIngestBatchResult batch) => new()
    {
        Success = batch.Success,
        ProviderId = batch.ProviderId,
        SourceId = batch.SourceId,
        TotalDocuments = batch.TotalDocuments,
        Succeeded = batch.Succeeded,
        Failed = batch.Failed,
        Message = batch.Message,
        Items = batch.Items.Select(i => new RagIngestItemResultDto
        {
            RelativePath = i.RelativePath,
            Success = i.Success,
            Message = i.Message,
            DocumentId = i.DocumentId
        }).ToList()
    };

    internal static RagProviderDockerStatusDto MapDocker(RagProviderDockerStatus status) => new()
    {
        ProviderId = status.ProviderId,
        ServiceName = status.ServiceName,
        DockerAvailable = status.DockerAvailable,
        ComposeFileFound = status.ComposeFileFound,
        ComposeFilePath = status.ComposeFilePath,
        State = status.State,
        StatusText = status.StatusText,
        ContainerId = status.ContainerId,
        ContainerName = status.ContainerName,
        Health = status.Health,
        Message = status.Message
    };

    private ConfiguredRagProviderDto BuildConfiguredDto()
    {
        var options = RagProviderConfigBuilder.FromConfiguration(_configuration);
        return new ConfiguredRagProviderDto
        {
            DefaultProvider = options.DefaultProvider,
            LightRAG = new LightRagProviderConfigDto
            {
                BaseUrl = options.LightRAG.BaseUrl,
                ApiKey = options.LightRAG.ApiKey,
                DefaultMode = options.LightRAG.DefaultMode.ToString(),
                Transport = options.LightRAG.Transport.ToString()
            },
            Cognee = new CogneeProviderConfigDto
            {
                BaseUrl = options.Cognee.BaseUrl,
                McpPath = options.Cognee.McpPath,
                SearchType = options.Cognee.SearchType,
                LlmApiKey = options.Cognee.LlmApiKey,
                Transport = options.Cognee.Transport.ToString()
            }
        };
    }

    private static AvailableRagProviderDto BuildAvailableDto(RagProviderDescriptor cat)
    {
        var fields = RagProviderConfigSchema.GetFields(cat.Id)
            .Select(f => new RagProviderConfigFieldDto
            {
                Key = f.Key,
                Label = f.Label,
                FieldType = f.FieldType,
                DefaultValue = f.DefaultValue,
                Placeholder = f.Placeholder,
                HelpText = f.HelpText,
                Required = f.Required
            })
            .ToList();

        return new AvailableRagProviderDto
        {
            Id = cat.Id,
            DisplayName = cat.DisplayName,
            Maturity = cat.Maturity,
            Summary = cat.Summary,
            Limitations = cat.Limitations,
            DeploymentNotes = cat.DeploymentNotes,
            Capabilities = cat.Capabilities.ToList(),
            ContextStrategies = cat.ContextStrategies.ToList(),
            RequiresDocker = cat.RequiresDocker,
            DockerServiceName = RagProviderConfigSchema.GetDockerServiceName(cat.Id),
            DefaultTransport = cat.DefaultTransport.ToString(),
            ConfigFields = fields
        };
    }

    private static string ResolveTransport(string providerId, ConfiguredRagProviderDto configured) =>
        RagProviderIds.Normalize(providerId) switch
        {
            RagProviderIds.LightRag => configured.LightRAG.Transport,
            RagProviderIds.Cognee => configured.Cognee.Transport,
            _ => "None"
        };

    private static string MapHealthOverall(RagHealthStatus status) =>
        status switch
        {
            RagHealthStatus.Healthy => "healthy",
            RagHealthStatus.Degraded => "degraded",
            _ => "degraded"
        };

    private static LightRagProviderOptions MapLightRagOptions(LightRagProviderConfigDto dto) => new()
    {
        BaseUrl = dto.BaseUrl?.Trim() ?? "http://127.0.0.1:9621",
        ApiKey = dto.ApiKey ?? "",
        DefaultMode = Enum.TryParse<RagQueryMode>(dto.DefaultMode, true, out var mode) ? mode : RagQueryMode.Hybrid,
        Transport = Enum.TryParse<RagTransportKind>(dto.Transport, true, out var transport) ? transport : RagTransportKind.Rest
    };

    private static CogneeProviderOptions MapCogneeOptions(CogneeProviderConfigDto dto) => new()
    {
        BaseUrl = dto.BaseUrl?.Trim() ?? "http://127.0.0.1:8000",
        McpPath = string.IsNullOrWhiteSpace(dto.McpPath) ? "/mcp" : dto.McpPath.Trim(),
        SearchType = string.IsNullOrWhiteSpace(dto.SearchType) ? "RAG_COMPLETION" : dto.SearchType.Trim(),
        LlmApiKey = dto.LlmApiKey ?? "",
        Transport = Enum.TryParse<RagTransportKind>(dto.Transport, true, out var transport) ? transport : RagTransportKind.McpHttp
    };

    private static RagQueryMode ParseQueryMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
            return RagQueryMode.Auto;

        return Enum.TryParse<RagQueryMode>(mode, true, out var parsed) ? parsed : RagQueryMode.Auto;
    }

    /// <summary>
    /// Dashboard test panel prefers fast chunk retrieval for Cognee (CHUNKS) instead of
    /// RAG_COMPLETION / GRAPH_COMPLETION, which can block the MCP server for many minutes.
    /// </summary>
    private static RagQueryMode ResolveDashboardQueryMode(string providerId, string? mode)
    {
        var parsed = ParseQueryMode(mode);
        if (parsed != RagQueryMode.Auto)
            return parsed;

        return RagProviderIds.Normalize(providerId) == RagProviderIds.Cognee
            ? RagQueryMode.Vector
            : RagQueryMode.Auto;
    }

    private static string BuildEmptyQueryMessage(string providerId, RagQueryResult result)
    {
        var canonical = RagProviderIds.Normalize(providerId);
        if (canonical == RagProviderIds.None)
            return "Query completed with no chunks (expected for Markdown only — no external index).";

        if (canonical == RagProviderIds.LightRag)
        {
            var detail = TryExtractLightRagQueryDetail(result.RawDebugJson);
            return string.IsNullOrWhiteSpace(detail)
                ? "LightRAG returned no chunks. Run Ingest data first. If you ingested before Ollama models were ready, click Ingest again or reprocess failed docs in LightRAG."
                : $"LightRAG returned no chunks: {detail}";
        }

        if (canonical == RagProviderIds.Cognee)
            return "Cognee returned no chunks. Run Ingest data first and wait for graph extraction to finish. Dashboard test queries use CHUNKS (fast retrieval); switch Search type to RAG_COMPLETION in settings for full LLM answers.";

        return "Query completed with no chunks.";
    }

    private static string? TryExtractLightRagQueryDetail(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            return null;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(rawJson);
            if (doc.RootElement.TryGetProperty("message", out var message))
            {
                var text = message.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                    return text.Trim();
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // ignore malformed debug payload
        }

        return null;
    }
}
