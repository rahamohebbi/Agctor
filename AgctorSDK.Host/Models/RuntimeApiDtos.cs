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
    public string? ProtoHost { get; set; }
    public int? ProtoPort { get; set; }
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
    public string Summary { get; set; } = null!;
    public string Limitations { get; set; } = null!;
    public string DeploymentNotes { get; set; } = null!;
    public IReadOnlyList<string> Capabilities { get; set; } = Array.Empty<string>();
    public bool SupportsProtoRemoting { get; set; }
    /// <summary>False when id is in factory but missing catalog copy (should not happen).</summary>
    public bool HasCatalogEntry { get; set; } = true;
}

/// <summary>PUT /api/runtime body.</summary>
public sealed class UpdateRuntimeSelectionDto
{
    public string DefaultRuntime { get; set; } = null!;
    public string? ProtoHost { get; set; }
    public int? ProtoPort { get; set; }
}

/// <summary>PUT /api/runtime response.</summary>
public sealed class UpdateRuntimeSelectionResponseDto
{
    public bool RequiresRestart { get; set; } = true;
    public string PersistedCanonicalRuntime { get; set; } = null!;
    public string Message { get; set; } = null!;
}
