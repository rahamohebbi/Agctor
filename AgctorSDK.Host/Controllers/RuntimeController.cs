using AgctorSDK.Host.Models;
using AgctorSDK.Host.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgctorSDK.Host.Controllers;

/// <summary>
/// Actor runtime dashboard API (PRD-012): live status, catalog, Docker sidecars, and Tier A persistence.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class RuntimeController : ControllerBase
{
    private readonly IRuntimeDashboardService _dashboard;
    private readonly IActorRuntimeDockerService _docker;
    private readonly ILogger<RuntimeController> _logger;

    public RuntimeController(
        IRuntimeDashboardService dashboard,
        IActorRuntimeDockerService docker,
        ILogger<RuntimeController> logger)
    {
        _dashboard = dashboard;
        _docker = docker;
        _logger = logger;
    }

    /// <summary>Live adapter, configured next-boot values, and catalog.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(RuntimeStatusResponseDto), StatusCodes.Status200OK)]
    public Task<ActionResult<RuntimeStatusResponseDto>> GetStatus(CancellationToken cancellationToken = default)
        => GetStatusInternal(cancellationToken);

    private async Task<ActionResult<RuntimeStatusResponseDto>> GetStatusInternal(CancellationToken cancellationToken)
    {
        var dto = await _dashboard.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        return Ok(dto);
    }

    /// <summary>Combined adapter + Docker health for monitoring.</summary>
    [HttpGet("health")]
    [ProducesResponseType(typeof(RuntimeHealthResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RuntimeHealthResponseDto>> GetHealth(CancellationToken cancellationToken = default)
    {
        var dto = await _dashboard.GetHealthAsync(cancellationToken).ConfigureAwait(false);
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

        try
        {
            var result = await _dashboard.SaveSelectionAsync(body, cancellationToken).ConfigureAwait(false);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorResponse { Code = "UNKNOWN_RUNTIME", Message = ex.Message });
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
    }

    /// <summary>Docker sidecar status for a runtime id (Orleans, Proto.Actor).</summary>
    [HttpGet("docker/{runtimeId}")]
    [ProducesResponseType(typeof(RuntimeDockerStatusDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RuntimeDockerStatusDto>> GetDockerStatus(string runtimeId, CancellationToken cancellationToken = default)
    {
        var status = await _docker.GetStatusAsync(runtimeId, cancellationToken).ConfigureAwait(false);
        return Ok(RuntimeDashboardService.MapDocker(status));
    }

    /// <summary>Pull/build Docker image for the runtime sidecar.</summary>
    [HttpPost("docker/{runtimeId}/install")]
    [ProducesResponseType(typeof(RuntimeDockerActionResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RuntimeDockerActionResponseDto>> InstallDocker(string runtimeId, CancellationToken cancellationToken = default)
        => await RunDockerActionAsync(runtimeId, _docker.InstallAsync, cancellationToken);

    /// <summary>Start Docker sidecar for the runtime.</summary>
    [HttpPost("docker/{runtimeId}/start")]
    [ProducesResponseType(typeof(RuntimeDockerActionResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RuntimeDockerActionResponseDto>> StartDocker(string runtimeId, CancellationToken cancellationToken = default)
        => await RunDockerActionAsync(runtimeId, _docker.StartAsync, cancellationToken);

    /// <summary>Stop Docker sidecar for the runtime.</summary>
    [HttpPost("docker/{runtimeId}/stop")]
    [ProducesResponseType(typeof(RuntimeDockerActionResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RuntimeDockerActionResponseDto>> StopDocker(string runtimeId, CancellationToken cancellationToken = default)
        => await RunDockerActionAsync(runtimeId, _docker.StopAsync, cancellationToken);

    private async Task<ActionResult<RuntimeDockerActionResponseDto>> RunDockerActionAsync(
        string runtimeId,
        Func<string, CancellationToken, Task<AgctorSDK.Core.Runtime.ActorRuntimeDockerActionResult>> action,
        CancellationToken cancellationToken)
    {
        var result = await action(runtimeId, cancellationToken).ConfigureAwait(false);
        return Ok(new RuntimeDockerActionResponseDto
        {
            Success = result.Success,
            Message = result.Message
        });
    }
}
