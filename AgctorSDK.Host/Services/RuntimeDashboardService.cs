using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Runtime;
using AgctorSDK.Host.Models;
using AgctorSDK.Host.Services;

namespace AgctorSDK.Host.Services;

/// <summary>
/// Shared read/write logic for the actor-runtime dashboard (API + Blazor components).
/// </summary>
public interface IRuntimeDashboardService
{
    Task<RuntimeStatusResponseDto> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<UpdateRuntimeSelectionResponseDto> SaveSelectionAsync(UpdateRuntimeSelectionDto body, CancellationToken cancellationToken = default);
    Task<RuntimeHealthResponseDto> GetHealthAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class RuntimeDashboardService : IRuntimeDashboardService
{
    private readonly IActorRuntimeAdapter _runtime;
    private readonly IActorRuntimeAdapterFactory _runtimeFactory;
    private readonly IConfiguration _configuration;
    private readonly IUserRuntimeSettingsService _userRuntimeSettings;
    private readonly IActorRuntimeDockerService _docker;
    private readonly ILogger<RuntimeDashboardService> _logger;

    public RuntimeDashboardService(
        IActorRuntimeAdapter runtime,
        IActorRuntimeAdapterFactory runtimeFactory,
        IConfiguration configuration,
        IUserRuntimeSettingsService userRuntimeSettings,
        IActorRuntimeDockerService docker,
        ILogger<RuntimeDashboardService> logger)
    {
        _runtime = runtime;
        _runtimeFactory = runtimeFactory;
        _configuration = configuration;
        _userRuntimeSettings = userRuntimeSettings;
        _docker = docker;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<RuntimeStatusResponseDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var canonical = RuntimeCanonicalId.FromAdapter(_runtime);
        RuntimeStatisticsDto? stats = null;
        try
        {
            var s = await _runtime.GetStatisticsAsync(cancellationToken).ConfigureAwait(false);
            stats = new RuntimeStatisticsDto
            {
                ActiveActorCount = s.ActiveActorCount,
                TotalMessagesProcessed = s.TotalMessagesProcessed,
                MessagesPerSecond = s.MessagesPerSecond,
                AverageMessageProcessingTimeMs = s.AverageMessageProcessingTime,
                UptimeSeconds = s.Uptime.TotalSeconds,
                MemoryUsageBytes = s.MemoryUsageBytes
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "GetStatisticsAsync not available for dashboard");
        }

        var configuredRuntime = _configuration.GetValue<string>("Agctor:DefaultRuntime", "InMemory") ?? "InMemory";
        var available = _runtimeFactory.GetAvailableRuntimes()
            .Select(BuildAvailableDto)
            .ToList();

        return new RuntimeStatusResponseDto
        {
            Current = new CurrentRuntimeDto
            {
                CanonicalId = canonical,
                AdapterName = _runtime.Name,
                Version = _runtime.Version,
                IsInitialized = _runtime.IsInitialized,
                Statistics = stats
            },
            Configured = BuildConfiguredDto(configuredRuntime),
            Available = available
        };
    }

    /// <inheritdoc />
    public async Task<UpdateRuntimeSelectionResponseDto> SaveSelectionAsync(
        UpdateRuntimeSelectionDto body,
        CancellationToken cancellationToken = default)
    {
        if (body == null)
            throw new ArgumentException("Request body is required.");

        if (!RuntimeSelectionNormalizer.TryNormalize(body.DefaultRuntime, _runtimeFactory, out var canonical, out var err))
            throw new InvalidOperationException(err ?? "Invalid runtime.");

        // Merge with effective config so picking a model card does not wipe other Agctor keys.
        var configured = BuildConfiguredDto(
            _configuration.GetValue<string>("Agctor:DefaultRuntime", "InMemory") ?? "InMemory");

        await _userRuntimeSettings.PersistAsync(new RuntimeSettingsUpdate
        {
            CanonicalRuntimeId = canonical,
            AllowExperimentalRuntimes = body.AllowExperimentalRuntimes ?? configured.AllowExperimentalRuntimes,
            ProtoHost = body.ProtoHost ?? configured.ProtoHost,
            ProtoPort = body.ProtoPort ?? configured.ProtoPort,
            OrleansClusterId = body.OrleansClusterId ?? configured.OrleansClusterId,
            OrleansServiceId = body.OrleansServiceId ?? configured.OrleansServiceId,
            OrleansGatewayHost = body.OrleansGatewayHost ?? configured.OrleansGatewayHost,
            OrleansGatewayPort = body.OrleansGatewayPort ?? configured.OrleansGatewayPort
        }, cancellationToken).ConfigureAwait(false);

        return new UpdateRuntimeSelectionResponseDto
        {
            RequiresRestart = true,
            PersistedCanonicalRuntime = canonical,
            Message = "Settings saved to appsettings.User.json. Restart the Host to apply the new actor runtime."
        };
    }

    /// <inheritdoc />
    public async Task<RuntimeHealthResponseDto> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        var liveId = RuntimeCanonicalId.FromAdapter(_runtime);
        var dockerStatus = await _docker.GetStatusAsync(liveId, cancellationToken).ConfigureAwait(false);
        var dockerDto = MapDocker(dockerStatus);

        var overall = "healthy";
        string? detail = null;

        if (!_runtime.IsInitialized)
        {
            overall = "degraded";
            detail = "Actor runtime adapter is not initialized.";
        }
        else if (ActorRuntimeConfigSchema.DockerBackedRuntimes.Contains(liveId)
                 && dockerStatus.State is not ("running" or "not_applicable"))
        {
            overall = "degraded";
            detail = dockerStatus.Message ?? "Docker sidecar is not running.";
        }

        return new RuntimeHealthResponseDto
        {
            LiveRuntimeId = liveId,
            AdapterInitialized = _runtime.IsInitialized,
            OverallStatus = overall,
            Docker = dockerDto,
            Detail = detail
        };
    }

    private ConfiguredRuntimeDto BuildConfiguredDto(string configuredRuntime) => new()
    {
        DefaultRuntime = configuredRuntime,
        AllowExperimentalRuntimes = _configuration.GetValue("Agctor:AllowExperimentalRuntimes", false),
        ProtoHost = _configuration.GetValue<string>("Agctor:ProtoHost"),
        ProtoPort = _configuration.GetValue<int?>("Agctor:ProtoPort"),
        OrleansClusterId = _configuration.GetValue<string>("Agctor:OrleansClusterId"),
        OrleansServiceId = _configuration.GetValue<string>("Agctor:OrleansServiceId"),
        OrleansGatewayHost = _configuration.GetValue<string>("Agctor:OrleansGatewayHost"),
        OrleansGatewayPort = _configuration.GetValue<int?>("Agctor:OrleansGatewayPort")
    };

    private static AvailableRuntimeDto BuildAvailableDto(string id)
    {
        var cat = ActorRuntimeCatalog.GetById(id);
        var fields = ActorRuntimeConfigSchema.GetFields(id)
            .Select(f => new RuntimeConfigFieldDto
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

        if (cat != null)
        {
            return new AvailableRuntimeDto
            {
                Id = cat.Id,
                DisplayName = cat.DisplayName,
                Maturity = cat.Maturity,
                Summary = cat.Summary,
                Limitations = cat.Limitations,
                DeploymentNotes = cat.DeploymentNotes,
                Capabilities = cat.Capabilities.ToList(),
                SupportsProtoRemoting = cat.SupportsProtoRemoting,
                RequiresDocker = ActorRuntimeConfigSchema.DockerBackedRuntimes.Contains(cat.Id),
                DockerServiceName = ActorRuntimeConfigSchema.GetDockerServiceName(cat.Id),
                ConfigFields = fields,
                HasCatalogEntry = true
            };
        }

        return new AvailableRuntimeDto
        {
            Id = id,
            DisplayName = id,
            Summary = "",
            Limitations = "",
            DeploymentNotes = "",
            Capabilities = Array.Empty<string>(),
            SupportsProtoRemoting = string.Equals(id, "Proto.Actor", StringComparison.OrdinalIgnoreCase),
            RequiresDocker = ActorRuntimeConfigSchema.DockerBackedRuntimes.Contains(id),
            DockerServiceName = ActorRuntimeConfigSchema.GetDockerServiceName(id),
            ConfigFields = fields,
            HasCatalogEntry = false
        };
    }

    internal static RuntimeDockerStatusDto MapDocker(ActorRuntimeDockerStatus status) => new()
    {
        RuntimeId = status.RuntimeId,
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
}
