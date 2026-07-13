using System.Text.Json;
using AgctorSDK.Host.Models;
using AgctorSDK.Host.Services;
using Microsoft.AspNetCore.Http.Features;
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
    private static readonly JsonSerializerOptions SseJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

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

    /// <summary>Run a validated terminal command and return stdout/stderr (buffered until exit).</summary>
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

    /// <summary>
    /// Stream stdout/stderr as SSE while the command runs (live docker pull progress).
    /// Events: <c>stdout</c>, <c>stderr</c>, <c>done</c>, <c>error</c>.
    /// </summary>
    [HttpPost("run/stream")]
    [Produces("text/event-stream")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task RunStream(
        [FromBody] RunTerminalCommandRequestDto body,
        CancellationToken cancellationToken = default)
    {
        if (body == null || string.IsNullOrWhiteSpace(body.Command))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsJsonAsync(new ErrorResponse { Code = "INVALID_BODY", Message = "Command is required." }, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (!_terminal.TryValidate(body.Command, out var validationError))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsJsonAsync(
                    new ErrorResponse { Code = "INVALID_COMMAND", Message = validationError ?? "Invalid command." },
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "text/event-stream; charset=utf-8";
        Response.Headers.CacheControl = "no-cache, no-transform";
        Response.Headers.Append("X-Accel-Buffering", "no");
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, HttpContext.RequestAborted);
        var ct = linked.Token;

        async Task WriteEventAsync(TerminalStreamEventDto evt)
        {
            var json = JsonSerializer.Serialize(evt, SseJson);
            await Response.WriteAsync($"data: {json}\n\n", ct).ConfigureAwait(false);
            await Response.Body.FlushAsync(ct).ConfigureAwait(false);
        }

        try
        {
            var result = await _terminal.RunStreamingAsync(
                    body.Command,
                    async (channel, text, token) =>
                    {
                        await WriteEventAsync(new TerminalStreamEventDto
                        {
                            Type = channel,
                            Text = text
                        }).ConfigureAwait(false);
                    },
                    ct)
                .ConfigureAwait(false);

            await WriteEventAsync(new TerminalStreamEventDto
            {
                Type = "done",
                Success = result.Success,
                ExitCode = result.ExitCode,
                Message = result.Message,
                Text = result.StdErr
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Client disconnected — nothing to write.
        }
        catch (Exception ex)
        {
            try
            {
                await WriteEventAsync(new TerminalStreamEventDto
                {
                    Type = "error",
                    Success = false,
                    ExitCode = -1,
                    Message = ex.Message
                }).ConfigureAwait(false);
            }
            catch
            {
                // Response may already be closed.
            }
        }
    }
}
