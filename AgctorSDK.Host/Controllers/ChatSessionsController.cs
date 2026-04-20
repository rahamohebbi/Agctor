using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Sessions.Models;
using AgctorSDK.Host.Models;
using Microsoft.AspNetCore.Mvc;

namespace AgctorSDK.Host.Controllers
{
    /// <summary>
    /// API endpoints for chat session lifecycle and transcript loading.
    /// </summary>
    [ApiController]
    [Route("api/chat/sessions")]
    [Produces("application/json")]
    public class ChatSessionsController : ControllerBase
    {
        private readonly ISessionStore _sessionStore;
        private readonly ILogger<ChatSessionsController> _logger;

        public ChatSessionsController(ISessionStore sessionStore, ILogger<ChatSessionsController> logger)
        {
            _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [HttpPost]
        [ProducesResponseType(typeof(SessionInfo), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<SessionInfo>> CreateAsync([FromBody] CreateChatSessionRequest? request, CancellationToken cancellationToken = default)
        {
            try
            {
                var created = await _sessionStore.CreateSessionAsync(request?.SessionId, request?.Title, request?.ProjectId, cancellationToken);
                return Created($"/api/chat/sessions/{created.SessionId}", created);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(ex, "Create chat session failed — project missing");
                return NotFound(new ErrorResponse { Code = "PROJECT_NOT_FOUND", Message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create chat session");
                return StatusCode(500, new ErrorResponse
                {
                    Code = "SESSION_CREATE_FAILED",
                    Message = ex.Message
                });
            }
        }

        [HttpGet]
        [ProducesResponseType(typeof(IReadOnlyList<SessionInfo>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<IReadOnlyList<SessionInfo>>> ListAsync(
            [FromQuery] int limit = 50,
            [FromQuery] int offset = 0,
            [FromQuery] string? projectId = null,
            [FromQuery] bool standalone = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(projectId) && standalone)
                {
                    return BadRequest(new ErrorResponse
                    {
                        Code = "SESSION_LIST_AMBIGUOUS",
                        Message = "Use either projectId or standalone, not both."
                    });
                }

                IReadOnlyList<SessionInfo> sessions;
                if (!string.IsNullOrWhiteSpace(projectId))
                    sessions = await _sessionStore.ListSessionsByProjectAsync(projectId.Trim(), limit, offset, cancellationToken);
                else if (standalone)
                    sessions = await _sessionStore.ListStandaloneSessionsAsync(limit, offset, cancellationToken);
                else
                    sessions = await _sessionStore.ListSessionsAsync(limit, offset, cancellationToken);

                return Ok(sessions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to list chat sessions");
                return StatusCode(500, new ErrorResponse
                {
                    Code = "SESSION_LIST_FAILED",
                    Message = ex.Message
                });
            }
        }

        /// <summary>Put session into a project (move from another project or standalone).</summary>
        [HttpPut("{sessionId}/project")]
        [ProducesResponseType(typeof(SessionInfo), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<SessionInfo>> PutProjectAsync(
            [FromRoute] string sessionId,
            [FromBody] AssignChatSessionProjectRequest? request,
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.ProjectId))
                {
                    return BadRequest(new ErrorResponse
                    {
                        Code = "SESSION_PROJECT_REQUIRED",
                        Message = "projectId is required in the request body."
                    });
                }

                var session = await _sessionStore.GetSessionAsync(sessionId, cancellationToken);
                if (session == null)
                {
                    return NotFound(new ErrorResponse
                    {
                        Code = "SESSION_NOT_FOUND",
                        Message = $"Session '{sessionId}' was not found."
                    });
                }

                await _sessionStore.AssignSessionToProjectAsync(sessionId, request.ProjectId.Trim(), cancellationToken);
                var updated = await _sessionStore.GetSessionAsync(sessionId, cancellationToken);
                return Ok(updated!);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Assign session {SessionId} to project failed", sessionId);
                if (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
                {
                    return NotFound(new ErrorResponse { Code = "PROJECT_NOT_FOUND", Message = ex.Message });
                }

                return BadRequest(new ErrorResponse { Code = "SESSION_ASSIGN_FAILED", Message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to assign session {SessionId} to project", sessionId);
                return StatusCode(500, new ErrorResponse { Code = "SESSION_ASSIGN_FAILED", Message = ex.Message });
            }
        }

        /// <summary>Remove session from its project (standalone session).</summary>
        [HttpDelete("{sessionId}/project")]
        [ProducesResponseType(typeof(SessionInfo), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<SessionInfo>> DeleteProjectAsync([FromRoute] string sessionId, CancellationToken cancellationToken = default)
        {
            try
            {
                var session = await _sessionStore.GetSessionAsync(sessionId, cancellationToken);
                if (session == null)
                {
                    return NotFound(new ErrorResponse
                    {
                        Code = "SESSION_NOT_FOUND",
                        Message = $"Session '{sessionId}' was not found."
                    });
                }

                await _sessionStore.DetachSessionFromProjectAsync(sessionId, cancellationToken);
                var updated = await _sessionStore.GetSessionAsync(sessionId, cancellationToken);
                return Ok(updated!);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Detach session {SessionId} from project failed", sessionId);
                return BadRequest(new ErrorResponse { Code = "SESSION_DETACH_FAILED", Message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to detach session {SessionId} from project", sessionId);
                return StatusCode(500, new ErrorResponse { Code = "SESSION_DETACH_FAILED", Message = ex.Message });
            }
        }

        /// <summary>Renames a session (updates <c>title</c>).</summary>
        [HttpPut("{sessionId}")]
        [ProducesResponseType(typeof(SessionInfo), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<SessionInfo>> UpdateAsync(
            [FromRoute] string sessionId,
            [FromBody] UpdateChatSessionRequest? request,
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.Title))
                {
                    return BadRequest(new ErrorResponse
                    {
                        Code = "SESSION_UPDATE_EMPTY",
                        Message = "Provide a non-empty title."
                    });
                }

                var existing = await _sessionStore.GetSessionAsync(sessionId, cancellationToken);
                if (existing == null)
                {
                    return NotFound(new ErrorResponse
                    {
                        Code = "SESSION_NOT_FOUND",
                        Message = $"Session '{sessionId}' was not found."
                    });
                }

                var updated = await _sessionStore.UpdateSessionTitleAsync(sessionId, request.Title, cancellationToken);
                return Ok(updated);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
            {
                return NotFound(new ErrorResponse { Code = "SESSION_NOT_FOUND", Message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new ErrorResponse { Code = "SESSION_UPDATE_INVALID", Message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update chat session {SessionId}", sessionId);
                return StatusCode(500, new ErrorResponse { Code = "SESSION_UPDATE_FAILED", Message = ex.Message });
            }
        }

        /// <summary>Deletes a session and all of its turns, trace links, summary, and project-move history.</summary>
        [HttpDelete("{sessionId}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteAsync([FromRoute] string sessionId, CancellationToken cancellationToken = default)
        {
            try
            {
                await _sessionStore.DeleteSessionAsync(sessionId, cancellationToken);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete chat session {SessionId}", sessionId);
                return StatusCode(500, new ErrorResponse { Code = "SESSION_DELETE_FAILED", Message = ex.Message });
            }
        }

        [HttpGet("{sessionId}")]
        [ProducesResponseType(typeof(SessionTranscript), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<SessionTranscript>> GetAsync(
            [FromRoute] string sessionId,
            [FromQuery] int? lastTurns = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var session = await _sessionStore.GetSessionAsync(sessionId, cancellationToken);
                if (session == null)
                {
                    return NotFound(new ErrorResponse
                    {
                        Code = "SESSION_NOT_FOUND",
                        Message = $"Session '{sessionId}' was not found."
                    });
                }

                var turns = await _sessionStore.GetTurnsAsync(sessionId, lastTurns, cancellationToken);
                var traceLinks = await _sessionStore.GetTraceLinksAsync(sessionId, cancellationToken);
                var summary = await _sessionStore.GetSummaryAsync(sessionId, cancellationToken);
                return Ok(new SessionTranscript
                {
                    Session = session,
                    Turns = turns,
                    TraceLinks = traceLinks,
                    Summary = summary
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load chat session {SessionId}", sessionId);
                return StatusCode(500, new ErrorResponse
                {
                    Code = "SESSION_LOAD_FAILED",
                    Message = ex.Message
                });
            }
        }
    }
}
