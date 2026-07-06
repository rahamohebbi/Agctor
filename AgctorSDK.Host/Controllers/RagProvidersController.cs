using AgctorSDK.Core.Rag;
using AgctorSDK.Host.Models;
using AgctorSDK.Host.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgctorSDK.Host.Controllers;

/// <summary>
/// RAG providers dashboard API (PRD-025): catalog, settings, health, Docker sidecars, test query.
/// </summary>
[ApiController]
[Route("api/rag-providers")]
[Produces("application/json")]
public class RagProvidersController : ControllerBase
{
    private readonly IRagProvidersDashboardService _dashboard;
    private readonly IRagProviderDockerService _docker;
    private readonly ILogger<RagProvidersController> _logger;

    public RagProvidersController(
        IRagProvidersDashboardService dashboard,
        IRagProviderDockerService docker,
        ILogger<RagProvidersController> logger)
    {
        _dashboard = dashboard;
        _docker = docker;
        _logger = logger;
    }

    /// <summary>Configured provider, catalog, and live health snapshot.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(RagProviderStatusResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RagProviderStatusResponseDto>> GetStatus(CancellationToken cancellationToken = default)
    {
        var dto = await _dashboard.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        return Ok(dto);
    }

    /// <summary>Combined provider + Docker health for monitoring.</summary>
    [HttpGet("health")]
    [ProducesResponseType(typeof(RagProviderHealthResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RagProviderHealthResponseDto>> GetHealth(CancellationToken cancellationToken = default)
    {
        var dto = await _dashboard.GetHealthAsync(cancellationToken).ConfigureAwait(false);
        return Ok(dto);
    }

    /// <summary>Persist provider selection and settings to appsettings.User.json.</summary>
    [HttpPut]
    [ProducesResponseType(typeof(UpdateRagProviderSelectionResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<UpdateRagProviderSelectionResponseDto>> UpdateSelection(
        [FromBody] UpdateRagProviderSelectionDto body,
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
            return BadRequest(new ErrorResponse { Code = "UNKNOWN_PROVIDER", Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist RAG provider selection");
            return StatusCode(500, new ErrorResponse
            {
                Code = "RAG_PERSIST_ERROR",
                Message = "Could not write appsettings.User.json. Check host permissions."
            });
        }
    }

    /// <summary>Operator test query against the configured or specified provider.</summary>
    [HttpPost("query")]
    [ProducesResponseType(typeof(RagProviderQueryResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RagProviderQueryResponseDto>> Query(
        [FromBody] RagProviderQueryRequestDto body,
        CancellationToken cancellationToken = default)
    {
        if (body == null)
            return BadRequest(new ErrorResponse { Code = "INVALID_BODY", Message = "Request body is required." });

        var result = await _dashboard.QueryAsync(body, cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }

    /// <summary>Lists ingest data sources (implemented + planned).</summary>
    [HttpGet("ingest/sources")]
    [ProducesResponseType(typeof(RagIngestSourcesResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RagIngestSourcesResponseDto>> GetIngestSources(CancellationToken cancellationToken = default)
    {
        var dto = await _dashboard.GetIngestSourcesAsync(cancellationToken).ConfigureAwait(false);
        return Ok(dto);
    }

    /// <summary>Preview how many documents a source would ingest (no provider calls).</summary>
    [HttpPost("ingest/preview")]
    [ProducesResponseType(typeof(RagProviderIngestPreviewResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RagProviderIngestPreviewResponseDto>> PreviewIngest(
        [FromBody] RagProviderIngestRequestDto body,
        CancellationToken cancellationToken = default)
    {
        if (body == null)
            return BadRequest(new ErrorResponse { Code = "INVALID_BODY", Message = "Request body is required." });

        var result = await _dashboard.PreviewIngestAsync(body, cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }

    /// <summary>Ingest documents from a modular source into the selected RAG provider sidecar.</summary>
    [HttpPost("ingest")]
    [ProducesResponseType(typeof(RagProviderIngestResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RagProviderIngestResponseDto>> Ingest(
        [FromBody] RagProviderIngestRequestDto body,
        CancellationToken cancellationToken = default)
    {
        if (body == null)
            return BadRequest(new ErrorResponse { Code = "INVALID_BODY", Message = "Request body is required." });

        var result = await _dashboard.IngestAsync(body, cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }

    /// <summary>Docker sidecar status for a provider id (LightRAG, Cognee).</summary>
    [HttpGet("docker/{providerId}")]
    [ProducesResponseType(typeof(RagProviderDockerStatusDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RagProviderDockerStatusDto>> GetDockerStatus(
        string providerId,
        CancellationToken cancellationToken = default)
    {
        var status = await _docker.GetStatusAsync(providerId, cancellationToken).ConfigureAwait(false);
        return Ok(RagProvidersDashboardService.MapDocker(status));
    }

    /// <summary>Pull Docker image for the provider sidecar.</summary>
    [HttpPost("docker/{providerId}/install")]
    [ProducesResponseType(typeof(RagProviderDockerActionResponseDto), StatusCodes.Status200OK)]
    public Task<ActionResult<RagProviderDockerActionResponseDto>> InstallDocker(
        string providerId,
        CancellationToken cancellationToken = default)
        => RunDockerActionAsync(providerId, _docker.InstallAsync, cancellationToken);

    /// <summary>Start Docker sidecar for the provider.</summary>
    [HttpPost("docker/{providerId}/start")]
    [ProducesResponseType(typeof(RagProviderDockerActionResponseDto), StatusCodes.Status200OK)]
    public Task<ActionResult<RagProviderDockerActionResponseDto>> StartDocker(
        string providerId,
        CancellationToken cancellationToken = default)
        => RunDockerActionAsync(providerId, _docker.StartAsync, cancellationToken);

    /// <summary>Stop Docker sidecar for the provider.</summary>
    [HttpPost("docker/{providerId}/stop")]
    [ProducesResponseType(typeof(RagProviderDockerActionResponseDto), StatusCodes.Status200OK)]
    public Task<ActionResult<RagProviderDockerActionResponseDto>> StopDocker(
        string providerId,
        CancellationToken cancellationToken = default)
        => RunDockerActionAsync(providerId, _docker.StopAsync, cancellationToken);

    private async Task<ActionResult<RagProviderDockerActionResponseDto>> RunDockerActionAsync(
        string providerId,
        Func<string, CancellationToken, Task<RagProviderDockerActionResult>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await action(providerId, cancellationToken).ConfigureAwait(false);
            return Ok(new RagProviderDockerActionResponseDto
            {
                Success = result.Success,
                Message = result.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RAG Docker action failed for {ProviderId}", providerId);
            return Ok(new RagProviderDockerActionResponseDto
            {
                Success = false,
                Message = ex.Message
            });
        }
    }
}
