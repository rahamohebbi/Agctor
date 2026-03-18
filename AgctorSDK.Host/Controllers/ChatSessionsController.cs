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
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<SessionInfo>> CreateAsync([FromBody] CreateChatSessionRequest? request, CancellationToken cancellationToken = default)
        {
            try
            {
                var created = await _sessionStore.CreateSessionAsync(request?.SessionId, request?.Title, cancellationToken);
                return Created($"/api/chat/sessions/{created.SessionId}", created);
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
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<IReadOnlyList<SessionInfo>>> ListAsync(
            [FromQuery] int limit = 50,
            [FromQuery] int offset = 0,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var sessions = await _sessionStore.ListSessionsAsync(limit, offset, cancellationToken);
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
                var summary = await _sessionStore.GetSummaryAsync(sessionId, cancellationToken);
                return Ok(new SessionTranscript
                {
                    Session = session,
                    Turns = turns,
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
