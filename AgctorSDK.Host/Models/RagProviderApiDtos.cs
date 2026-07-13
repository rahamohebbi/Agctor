namespace AgctorSDK.Host.Models;

/// <summary>GET /api/rag-providers response (PRD-025 Phase 4).</summary>
public sealed class RagProviderStatusResponseDto
{
    public CurrentRagProviderDto Current { get; set; } = null!;
    public ConfiguredRagProviderDto Configured { get; set; } = null!;
    public IReadOnlyList<AvailableRagProviderDto> Available { get; set; } = Array.Empty<AvailableRagProviderDto>();
}

/// <summary>Effective configured default provider and per-provider settings.</summary>
public sealed class CurrentRagProviderDto
{
    public string ProviderId { get; set; } = null!;
    public string Transport { get; set; } = null!;
    public string HealthStatus { get; set; } = "unknown";
    public string? HealthMessage { get; set; }
}

/// <summary>Values from merged configuration (appsettings + User).</summary>
public sealed class ConfiguredRagProviderDto
{
    public string DefaultProvider { get; set; } = null!;
    public LightRagProviderConfigDto LightRAG { get; set; } = new();
    public GraphitiProviderConfigDto Graphiti { get; set; } = new();
    public CogneeProviderConfigDto Cognee { get; set; } = new();
}

/// <summary>LightRAG settings for dashboard forms.</summary>
public sealed class LightRagProviderConfigDto
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:9621";
    public string ApiKey { get; set; } = "";
    public string DefaultMode { get; set; } = "Hybrid";
    public string Transport { get; set; } = "Rest";
}

/// <summary>Graphiti REST settings for dashboard forms.</summary>
public sealed class GraphitiProviderConfigDto
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:8001";
    public string ApiKey { get; set; } = "";
    public string DefaultGroupId { get; set; } = "agctor";
    public string Transport { get; set; } = "Rest";
}

/// <summary>Cognee settings for dashboard forms.</summary>
public sealed class CogneeProviderConfigDto
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:8000";
    public string McpPath { get; set; } = "/mcp";
    public string SearchType { get; set; } = "RAG_COMPLETION";
    public string LlmApiKey { get; set; } = "";
    public string Transport { get; set; } = "McpHttp";
}

/// <summary>One catalog row for the provider grid.</summary>
public sealed class AvailableRagProviderDto
{
    public string Id { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string Maturity { get; set; } = null!;
    public string Summary { get; set; } = null!;
    public string Limitations { get; set; } = null!;
    public string DeploymentNotes { get; set; } = null!;
    public IReadOnlyList<string> Capabilities { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> ContextStrategies { get; set; } = Array.Empty<string>();
    public bool RequiresDocker { get; set; }
    public string? DockerServiceName { get; set; }
    public string DefaultTransport { get; set; } = null!;
    public IReadOnlyList<RagProviderConfigFieldDto> ConfigFields { get; set; } = Array.Empty<RagProviderConfigFieldDto>();
}

/// <summary>Config field metadata for dashboard forms.</summary>
public sealed class RagProviderConfigFieldDto
{
    public string Key { get; set; } = null!;
    public string Label { get; set; } = null!;
    public string FieldType { get; set; } = "text";
    public string? DefaultValue { get; set; }
    public string? Placeholder { get; set; }
    public string? HelpText { get; set; }
    public bool Required { get; set; }
}

/// <summary>PUT /api/rag-providers body.</summary>
public sealed class UpdateRagProviderSelectionDto
{
    public string DefaultProvider { get; set; } = null!;
    public LightRagProviderConfigDto? LightRAG { get; set; }
    public GraphitiProviderConfigDto? Graphiti { get; set; }
    public CogneeProviderConfigDto? Cognee { get; set; }
}

/// <summary>PUT /api/rag-providers response.</summary>
public sealed class UpdateRagProviderSelectionResponseDto
{
    public string PersistedProviderId { get; set; } = null!;
    public string Message { get; set; } = null!;
}

/// <summary>GET /api/rag-providers/health.</summary>
public sealed class RagProviderHealthResponseDto
{
    public string ProviderId { get; set; } = null!;
    public string OverallStatus { get; set; } = "unknown";
    public string? ProviderHealthStatus { get; set; }
    public string? Detail { get; set; }
    public RagProviderDockerStatusDto? Docker { get; set; }
}

/// <summary>POST /api/rag-providers/query body.</summary>
public sealed class RagProviderQueryRequestDto
{
    public string Query { get; set; } = null!;
    public string? CollectionId { get; set; }
    public int TopK { get; set; } = 8;
    public string? ProviderId { get; set; }
    public string? Mode { get; set; }
}

/// <summary>POST /api/rag-providers/query response.</summary>
public sealed class RagProviderQueryResponseDto
{
    public bool Success { get; set; }
    public string ProviderId { get; set; } = null!;
    public string Message { get; set; } = null!;
    public IReadOnlyList<RagContextChunkDto> Chunks { get; set; } = Array.Empty<RagContextChunkDto>();
}

/// <summary>One retrieved chunk for the test query panel.</summary>
public sealed class RagContextChunkDto
{
    public string Text { get; set; } = null!;
    public double? Score { get; set; }
    public string? SourcePath { get; set; }
}

/// <summary>GET /api/rag-providers/docker/{providerId}.</summary>
public sealed class RagProviderDockerStatusDto
{
    public string ProviderId { get; set; } = null!;
    public string? ServiceName { get; set; }
    public bool DockerAvailable { get; set; }
    public bool ComposeFileFound { get; set; }
    public string? ComposeFilePath { get; set; }
    public string State { get; set; } = "unknown";
    public string? StatusText { get; set; }
    public string? ContainerId { get; set; }
    public string? ContainerName { get; set; }
    public string? Health { get; set; }
    public string? Message { get; set; }
}

/// <summary>POST /api/rag-providers/docker/{providerId}/* action result.</summary>
public sealed class RagProviderDockerActionResponseDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = null!;
}

/// <summary>GET /api/rag-providers/ingest/sources.</summary>
public sealed class RagIngestSourcesResponseDto
{
    public IReadOnlyList<RagIngestSourceDto> Sources { get; set; } = Array.Empty<RagIngestSourceDto>();
    public string? ProjectRoot { get; set; }
    public bool ProjectRootConfigured { get; set; }
}

/// <summary>One ingest source row for the dashboard picker.</summary>
public sealed class RagIngestSourceDto
{
    public string Id { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string Description { get; set; } = null!;
    public bool IsImplemented { get; set; }
}

/// <summary>POST /api/rag-providers/ingest/preview and /ingest body.</summary>
public sealed class RagProviderIngestRequestDto
{
    public string SourceId { get; set; } = "agctor_markdown";
    public string? ProviderId { get; set; }
    public string? CollectionId { get; set; }
    public string? ProjectRoot { get; set; }
    /// <summary>When true, Cognee re-runs cognify on datasets that already exist.</summary>
    public bool ForceReingest { get; set; }
}

/// <summary>POST /api/rag-providers/ingest/preview response.</summary>
public sealed class RagProviderIngestPreviewResponseDto
{
    public bool Success { get; set; }
    public string SourceId { get; set; } = null!;
    public int DocumentCount { get; set; }
    public int DatasetBatchCount { get; set; }
    public IReadOnlyList<string> SamplePaths { get; set; } = Array.Empty<string>();
    public string Message { get; set; } = null!;
}

/// <summary>POST /api/rag-providers/ingest response.</summary>
public sealed class RagProviderIngestResponseDto
{
    public bool Success { get; set; }
    public string ProviderId { get; set; } = null!;
    public string SourceId { get; set; } = null!;
    public int TotalDocuments { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public string Message { get; set; } = null!;
    public IReadOnlyList<RagIngestItemResultDto> Items { get; set; } = Array.Empty<RagIngestItemResultDto>();
}

/// <summary>Per-file ingest result for dashboard detail list.</summary>
public sealed class RagIngestItemResultDto
{
    public string RelativePath { get; set; } = null!;
    public bool Success { get; set; }
    public string Message { get; set; } = null!;
    public string? DocumentId { get; set; }
}
