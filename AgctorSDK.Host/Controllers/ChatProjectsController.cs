using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Sessions.Models;
using AgctorSDK.Host.Models;
using AgctorSDK.Host.Services.Scenarios;
using Microsoft.AspNetCore.Mvc;

namespace AgctorSDK.Host.Controllers;

/// <summary>
/// REST API for chat project buckets (one project, many sessions).
/// </summary>
[ApiController]
[Route("api/chat/projects")]
[Produces("application/json")]
public sealed class ChatProjectsController : ControllerBase
{
    private readonly ISessionStore _sessionStore;
    private readonly IScenarioCatalog _scenarioCatalog;
    private readonly ILogger<ChatProjectsController> _logger;

    public ChatProjectsController(ISessionStore sessionStore, IScenarioCatalog scenarioCatalog, ILogger<ChatProjectsController> logger)
    {
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        _scenarioCatalog = scenarioCatalog ?? throw new ArgumentNullException(nameof(scenarioCatalog));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [HttpPost]
    [ProducesResponseType(typeof(SessionProject), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<SessionProject>> CreateAsync([FromBody] CreateChatProjectRequest? request, CancellationToken cancellationToken = default)
    {
        try
        {
            var scenarioId = ResolveScenarioId(request?.ScenarioId);
            if (_scenarioCatalog.Get(scenarioId) == null)
                return BadRequest(new ErrorResponse { Code = "SCENARIO_NOT_FOUND", Message = $"Scenario '{scenarioId}' was not found." });

            var created = await _sessionStore.CreateProjectAsync(request?.ProjectId, request?.Name, scenarioId, cancellationToken)
                .ConfigureAwait(false);
            return Created($"/api/chat/projects/{created.ProjectId}", created);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create chat project");
            return StatusCode(500, new ErrorResponse { Code = "PROJECT_CREATE_FAILED", Message = ex.Message });
        }
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SessionProject>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<IReadOnlyList<SessionProject>>> ListAsync(
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var list = await _sessionStore.ListProjectsAsync(limit, offset, cancellationToken).ConfigureAwait(false);
            return Ok(list);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list chat projects");
            return StatusCode(500, new ErrorResponse { Code = "PROJECT_LIST_FAILED", Message = ex.Message });
        }
    }

    [HttpGet("{projectId}")]
    [ProducesResponseType(typeof(SessionProject), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<SessionProject>> GetAsync([FromRoute] string projectId, CancellationToken cancellationToken = default)
    {
        try
        {
            var p = await _sessionStore.GetProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
            if (p == null)
            {
                return NotFound(new ErrorResponse
                {
                    Code = "PROJECT_NOT_FOUND",
                    Message = $"Project '{projectId}' was not found."
                });
            }

            return Ok(p);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load chat project {ProjectId}", projectId);
            return StatusCode(500, new ErrorResponse { Code = "PROJECT_LOAD_FAILED", Message = ex.Message });
        }
    }

    [HttpPut("{projectId}")]
    [ProducesResponseType(typeof(SessionProject), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<SessionProject>> UpdateAsync(
        [FromRoute] string projectId,
        [FromBody] UpdateChatProjectRequest? request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var existing = await _sessionStore.GetProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
            if (existing == null)
            {
                return NotFound(new ErrorResponse
                {
                    Code = "PROJECT_NOT_FOUND",
                    Message = $"Project '{projectId}' was not found."
                });
            }

            if (request == null || (string.IsNullOrWhiteSpace(request.Name) && string.IsNullOrWhiteSpace(request.ScenarioId)))
            {
                return BadRequest(new ErrorResponse
                {
                    Code = "PROJECT_UPDATE_EMPTY",
                    Message = "Provide at least one of name or scenarioId."
                });
            }
            var req = request;

            var scenarioId = ResolveScenarioId(req.ScenarioId, existing.ScenarioId);
            if (_scenarioCatalog.Get(scenarioId) == null)
                return BadRequest(new ErrorResponse { Code = "SCENARIO_NOT_FOUND", Message = $"Scenario '{scenarioId}' was not found." });

            var merged = new SessionProject
            {
                ProjectId = existing.ProjectId,
                Name = string.IsNullOrWhiteSpace(req.Name) ? existing.Name : req.Name.Trim(),
                ScenarioId = scenarioId,
                CreatedAt = existing.CreatedAt,
                SessionCount = existing.SessionCount
            };

            var updated = await _sessionStore.UpdateProjectAsync(merged, cancellationToken).ConfigureAwait(false);
            return Ok(updated);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Chat project update failed for {ProjectId}", projectId);
            return NotFound(new ErrorResponse { Code = "PROJECT_NOT_FOUND", Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update chat project {ProjectId}", projectId);
            return StatusCode(500, new ErrorResponse { Code = "PROJECT_UPDATE_FAILED", Message = ex.Message });
        }
    }

    [HttpDelete("{projectId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeleteAsync([FromRoute] string projectId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _sessionStore.DeleteProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete chat project {ProjectId}", projectId);
            return StatusCode(500, new ErrorResponse { Code = "PROJECT_DELETE_FAILED", Message = ex.Message });
        }
    }

    /// <summary>Sessions belonging to this project (newest activity first).</summary>
    [HttpGet("{projectId}/sessions")]
    [ProducesResponseType(typeof(IReadOnlyList<SessionInfo>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<IReadOnlyList<SessionInfo>>> ListSessionsAsync(
        [FromRoute] string projectId,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (await _sessionStore.GetProjectAsync(projectId, cancellationToken).ConfigureAwait(false) == null)
            {
                return NotFound(new ErrorResponse
                {
                    Code = "PROJECT_NOT_FOUND",
                    Message = $"Project '{projectId}' was not found."
                });
            }

            var sessions = await _sessionStore.ListSessionsByProjectAsync(projectId, limit, offset, cancellationToken).ConfigureAwait(false);
            return Ok(sessions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list sessions for project {ProjectId}", projectId);
            return StatusCode(500, new ErrorResponse { Code = "PROJECT_SESSIONS_LIST_FAILED", Message = ex.Message });
        }
    }

    private static string ResolveScenarioId(string? scenarioId, string? fallback = null)
    {
        if (!string.IsNullOrWhiteSpace(scenarioId)) return scenarioId.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(fallback)) return fallback.Trim().ToLowerInvariant();
        return SessionProjectTypes.People;
    }
}
