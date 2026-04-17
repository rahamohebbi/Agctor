using AgctorSDK.Core.Agents;
using AgctorSDK.Host.Models;
using AgctorSDK.Host.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgctorSDK.Host.Controllers;

/// <summary>
/// Dashboard APIs for listing local Ollama models and setting the global default (PRD-015).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class LlmController : ControllerBase
{
    private readonly IOllamaModelCatalog _catalog;
    private readonly ILlmUserSettingsService _userSettings;
    private readonly ILogger<LlmController> _logger;

    public LlmController(
        IOllamaModelCatalog catalog,
        ILlmUserSettingsService userSettings,
        ILogger<LlmController> logger)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _userSettings = userSettings ?? throw new ArgumentNullException(nameof(userSettings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Lists models from Ollama <c>/api/tags</c> for the configured base URL.</summary>
    [HttpGet("models")]
    [ProducesResponseType(typeof(LlmModelsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<LlmModelsResponse>> GetModels(CancellationToken cancellationToken = default)
    {
        try
        {
            var items = await _catalog.ListLocalModelsAsync(cancellationToken).ConfigureAwait(false);
            var dto = new LlmModelsResponse
            {
                Models = items.Select(m => new LlmModelItemDto
                {
                    Name = m.Name,
                    Size = m.Size,
                    ModifiedAt = m.ModifiedAt
                }).ToList()
            };
            return Ok(dto);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to list Ollama models");
            return StatusCode(502, new ErrorResponse
            {
                Code = "OLLAMA_UNREACHABLE",
                Message = "Could not reach Ollama at the configured URL. Ensure Ollama is running and Agctor:LLM:OllamaApiUrl is correct."
            });
        }
    }

    /// <summary>Persists <c>Agctor:LLM:DefaultModel</c> and applies it via <see cref="LLMAgent.ConfigureDefaults"/>.</summary>
    [HttpPut("default-model")]
    [ProducesResponseType(typeof(SetLlmDefaultModelResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SetLlmDefaultModelResponse>> PutDefaultModel(
        [FromBody] LlmDefaultModelRequest body,
        CancellationToken cancellationToken = default)
    {
        if (body == null || string.IsNullOrWhiteSpace(body.Model))
        {
            return BadRequest(new ErrorResponse
            {
                Code = "INVALID_MODEL",
                Message = "Model is required and must be non-empty."
            });
        }

        var normalized = body.Model.Trim();
        string? warning = null;
        try
        {
            var list = await _catalog.ListLocalModelsAsync(cancellationToken).ConfigureAwait(false);
            if (list.Count > 0 && !list.Any(m => string.Equals(m.Name, normalized, StringComparison.Ordinal)))
            {
                warning =
                    "Model was not found in the local Ollama catalog; the default was still applied. Pull the model or pick another name if generation fails.";
            }
        }
        catch (Exception ex)
        {
            // Ollama down: still persist and apply (PRD-015).
            _logger.LogDebug(ex, "Could not verify model against Ollama catalog; applying default anyway");
        }

        await _userSettings.PersistDefaultModelAsync(normalized, cancellationToken).ConfigureAwait(false);
        LLMAgent.ConfigureDefaults(LLMAgent.GetConfiguredOllamaApiUrl(), normalized);

        return Ok(new SetLlmDefaultModelResponse { Warning = warning });
    }
}
