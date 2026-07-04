using AgctorSDK.Host.Models;
using AgctorSDK.Host.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgctorSDK.Host.Controllers;

/// <summary>
/// API for the reusable terminal command panel (run validated docker compose commands from the dashboard).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class TerminalController : ControllerBase
{
    private readonly ITerminalCommandService _terminal;

    public TerminalController(ITerminalCommandService terminal)
    {
        _terminal = terminal;
    }

    /// <summary>Presets for a context (e.g. actor-runtime + Orleans).</summary>
    [HttpGet("presets")]
    [ProducesResponseType(typeof(TerminalCommandPresetsResponseDto), StatusCodes.Status200OK)]
    public ActionResult<TerminalCommandPresetsResponseDto> GetPresets(
        [FromQuery] string contextType = "actor-runtime",
        [FromQuery] string? contextKey = null)
    {
        var presets = _terminal.GetPresets(contextType, contextKey);
        return Ok(new TerminalCommandPresetsResponseDto
        {
            Presets = presets,
            DefaultCommand = _terminal.GetDefaultCommand(contextType, contextKey)
        });
    }

    /// <summary>Run a validated terminal command and return stdout/stderr.</summary>
    [HttpPost("run")]
    [ProducesResponseType(typeof(RunTerminalCommandResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RunTerminalCommandResponseDto>> Run(
        [FromBody] RunTerminalCommandRequestDto body,
        CancellationToken cancellationToken = default)
    {
        if (body == null || string.IsNullOrWhiteSpace(body.Command))
            return BadRequest(new ErrorResponse { Code = "INVALID_BODY", Message = "Command is required." });

        if (!_terminal.TryValidate(body.Command, out var validationError))
            return BadRequest(new ErrorResponse { Code = "INVALID_COMMAND", Message = validationError ?? "Invalid command." });

        var result = await _terminal.RunAsync(body.Command, cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }
}
