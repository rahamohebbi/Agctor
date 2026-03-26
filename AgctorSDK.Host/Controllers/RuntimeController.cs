using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Runtime;
using AgctorSDK.Host.Models;
using AgctorSDK.Host.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgctorSDK.Host.Controllers;

/// <summary>
/// Actor runtime dashboard API (PRD-012): live status, catalog, and Tier A persistence.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class RuntimeController : ControllerBase
{
    private readonly IActorRuntimeAdapter _runtime;
    private readonly IActorRuntimeAdapterFactory _runtimeFactory;
    private readonly IConfiguration _configuration;
    private readonly IUserRuntimeSettingsService _userRuntimeSettings;
    private readonly ILogger<RuntimeController> _logger;

    public RuntimeController(
        IActorRuntimeAdapter runtime,
        IActorRuntimeAdapterFactory runtimeFactory,
        IConfiguration configuration,
        IUserRuntimeSettingsService userRuntimeSettings,
        ILogger<RuntimeController> logger)
    {
        _runtime = runtime;
        _runtimeFactory = runtimeFactory;
        _configuration = configuration;
        _userRuntimeSettings = userRuntimeSettings;
        _logger = logger;
    }

    /// <summary>Live adapter, configured next-boot values, and catalog.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(RuntimeStatusResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RuntimeStatusResponseDto>> GetStatus(CancellationToken cancellationToken = default)
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
            .Select(id =>
            {
                var cat = ActorRuntimeCatalog.GetById(id);
                if (cat != null)
                {
                    return new AvailableRuntimeDto
                    {
                        Id = cat.Id,
                        DisplayName = cat.DisplayName,
                        Summary = cat.Summary,
                        Limitations = cat.Limitations,
                        DeploymentNotes = cat.DeploymentNotes,
                        Capabilities = cat.Capabilities.ToList(),
                        SupportsProtoRemoting = cat.SupportsProtoRemoting,
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
                    HasCatalogEntry = false
                };
            })
            .ToList();

        var dto = new RuntimeStatusResponseDto
        {
            Current = new CurrentRuntimeDto
            {
                CanonicalId = canonical,
                AdapterName = _runtime.Name,
                Version = _runtime.Version,
                IsInitialized = _runtime.IsInitialized,
                Statistics = stats
            },
            Configured = new ConfiguredRuntimeDto
            {
                DefaultRuntime = configuredRuntime,
                ProtoHost = _configuration.GetValue<string>("Agctor:ProtoHost"),
                ProtoPort = _configuration.GetValue<int?>("Agctor:ProtoPort")
            },
            Available = available
        };

        return Ok(dto);
    }

    /// <summary>Persist runtime selection for next Host start (restart required).</summary>
    [HttpPut]
    [ProducesResponseType(typeof(UpdateRuntimeSelectionResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<UpdateRuntimeSelectionResponseDto>> UpdateSelection(
        [FromBody] UpdateRuntimeSelectionDto body,
        CancellationToken cancellationToken = default)
    {
        if (body == null)
            return BadRequest(new ErrorResponse { Code = "INVALID_BODY", Message = "Request body is required." });

        if (!RuntimeSelectionNormalizer.TryNormalize(body.DefaultRuntime, _runtimeFactory, out var canonical, out var err))
            return BadRequest(new ErrorResponse { Code = "UNKNOWN_RUNTIME", Message = err ?? "Invalid runtime." });

        try
        {
            await _userRuntimeSettings.PersistAsync(canonical, body.ProtoHost, body.ProtoPort, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist runtime selection");
            return StatusCode(500, new ErrorResponse
            {
                Code = "RUNTIME_PERSIST_ERROR",
                Message = "Could not write appsettings.User.json. Check host permissions."
            });
        }

        return Ok(new UpdateRuntimeSelectionResponseDto
        {
            RequiresRestart = true,
            PersistedCanonicalRuntime = canonical,
            Message = "Settings saved. Restart the Host process to apply the new actor runtime."
        });
    }
}
