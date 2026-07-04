namespace AgctorSDK.Host.Models;

/// <summary>GET /api/runtime response (PRD-012).</summary>
public sealed class RuntimeStatusResponseDto
{
    public CurrentRuntimeDto Current { get; set; } = null!;
    public ConfiguredRuntimeDto Configured { get; set; } = null!;
    public IReadOnlyList<AvailableRuntimeDto> Available { get; set; } = Array.Empty<AvailableRuntimeDto>();
}

/// <summary>Live adapter state.</summary>
public sealed class CurrentRuntimeDto
{
    /// <summary>Factory id: InMemory, Orleans, Proto.Actor.</summary>
    public string CanonicalId { get; set; } = null!;
    public string AdapterName { get; set; } = null!;
    public string Version { get; set; } = null!;
    public bool IsInitialized { get; set; }
    public RuntimeStatisticsDto? Statistics { get; set; }
}

/// <summary>Effective configuration (next boot / merged files).</summary>
public sealed class ConfiguredRuntimeDto
{
    public string DefaultRuntime { get; set; } = null!;
    public bool AllowExperimentalRuntimes { get; set; }
    public string? ProtoHost { get; set; }
    public int? ProtoPort { get; set; }
    public string? OrleansClusterId { get; set; }
    public string? OrleansServiceId { get; set; }
    public string? OrleansGatewayHost { get; set; }
    public int? OrleansGatewayPort { get; set; }
}

/// <summary>Subset of IRuntimeStatistics for JSON.</summary>
public sealed class RuntimeStatisticsDto
{
    public int ActiveActorCount { get; set; }
    public long TotalMessagesProcessed { get; set; }
    public double MessagesPerSecond { get; set; }
    public double AverageMessageProcessingTimeMs { get; set; }
    public double UptimeSeconds { get; set; }
    public long MemoryUsageBytes { get; set; }
}

/// <summary>One catalog row plus factory membership.</summary>
public sealed class AvailableRuntimeDto
{
    public string Id { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string Maturity { get; set; } = null!;
    public string Summary { get; set; } = null!;
    public string Limitations { get; set; } = null!;
    public string DeploymentNotes { get; set; } = null!;
    public IReadOnlyList<string> Capabilities { get; set; } = Array.Empty<string>();
    public bool SupportsProtoRemoting { get; set; }
    public bool RequiresDocker { get; set; }
    public string? DockerServiceName { get; set; }
    public IReadOnlyList<RuntimeConfigFieldDto> ConfigFields { get; set; } = Array.Empty<RuntimeConfigFieldDto>();
    /// <summary>False when id is in factory but missing catalog copy (should not happen).</summary>
    public bool HasCatalogEntry { get; set; } = true;
}

/// <summary>Config field metadata for dashboard forms.</summary>
public sealed class RuntimeConfigFieldDto
{
    public string Key { get; set; } = null!;
    public string Label { get; set; } = null!;
    public string FieldType { get; set; } = "text";
    public string? DefaultValue { get; set; }
    public string? Placeholder { get; set; }
    public string? HelpText { get; set; }
    public bool Required { get; set; }
}

/// <summary>PUT /api/runtime body.</summary>
public sealed class UpdateRuntimeSelectionDto
{
    public string DefaultRuntime { get; set; } = null!;
    public bool? AllowExperimentalRuntimes { get; set; }
    public string? ProtoHost { get; set; }
    public int? ProtoPort { get; set; }
    public string? OrleansClusterId { get; set; }
    public string? OrleansServiceId { get; set; }
    public string? OrleansGatewayHost { get; set; }
    public int? OrleansGatewayPort { get; set; }
}

/// <summary>PUT /api/runtime response.</summary>
public sealed class UpdateRuntimeSelectionResponseDto
{
    public bool RequiresRestart { get; set; } = true;
    public string PersistedCanonicalRuntime { get; set; } = null!;
    public string Message { get; set; } = null!;
}

/// <summary>GET /api/runtime/docker/{runtimeId}.</summary>
public sealed class RuntimeDockerStatusDto
{
    public string RuntimeId { get; set; } = null!;
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

/// <summary>POST /api/runtime/docker/{runtimeId}/* action result.</summary>
public sealed class RuntimeDockerActionResponseDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = null!;
}

/// <summary>GET /api/runtime/health.</summary>
public sealed class RuntimeHealthResponseDto
{
    public string LiveRuntimeId { get; set; } = null!;
    public bool AdapterInitialized { get; set; }
    public string OverallStatus { get; set; } = "unknown";
    public RuntimeDockerStatusDto? Docker { get; set; }
    public string? Detail { get; set; }
}
