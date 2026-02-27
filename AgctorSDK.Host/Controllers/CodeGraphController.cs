using AgctorSDK.Host.Models;
using AgctorSDK.Host.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgctorSDK.Host.Controllers;

/// <summary>
/// Exposes current CodeGraph context for the dashboard when code-graph-demo scenario is active (PRD-006).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class CodeGraphController : ControllerBase
{
    private readonly ICodeGraphContextAccessor _contextAccessor;
    private readonly ILogger<CodeGraphController> _logger;

    public CodeGraphController(
        ICodeGraphContextAccessor contextAccessor,
        ILogger<CodeGraphController> logger)
    {
        _contextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Returns the current CodeGraph context (actor tree + embedding summary) when code-graph-demo has been set up; otherwise 404.
    /// </summary>
    [HttpGet("current")]
    [ProducesResponseType(typeof(CodeGraphContextDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<CodeGraphContextDto> GetCurrent()
    {
        var context = _contextAccessor.GetCurrent();
        if (context == null)
        {
            _logger.LogDebug("CodeGraph context requested but no active context");
            return NotFound();
        }
        return Ok(context);
    }
}
