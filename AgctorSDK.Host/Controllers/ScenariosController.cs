using AgctorSDK.Host.Models;
using AgctorSDK.Host.Services;
using AgctorSDK.Host.Services.Scenarios;
using Microsoft.AspNetCore.Mvc;

namespace AgctorSDK.Host.Controllers;

[ApiController]
[Route("api/scenarios")]
[Produces("application/json")]
public sealed class ScenariosController : ControllerBase
{
    private readonly IScenarioCatalog _catalog;
    private readonly IScenarioApplicationService _scenarioApplication;
    private readonly IScenarioFlowExecutionService _flowExecution;
    private readonly ILogger<ScenariosController> _logger;

    public ScenariosController(
        IScenarioCatalog catalog,
        IScenarioApplicationService scenarioApplication,
        IScenarioFlowExecutionService flowExecution,
        ILogger<ScenariosController> logger)
    {
        _catalog = catalog;
        _scenarioApplication = scenarioApplication;
        _flowExecution = flowExecution;
        _logger = logger;
    }

    /// <summary>
    /// Apply a scenario (spawn/configure actors). Id <c>default</c> uses <c>Agctor:Dashboard:ScenarioName</c>. Prefer this over <c>POST /api/Test/setup-scenario</c> (PRD-013 Phase 4).
    /// </summary>
    [HttpPost("{id}/apply")]
    [ProducesResponseType(typeof(ScenarioSetupResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ScenarioSetupResponse>> ApplyScenarioAsync(
        [FromRoute] string id,
        [FromBody] ScenarioApplyRequest? body,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new ErrorResponse
            {
                Code = "INVALID_SCENARIO_ID",
                Message = "Scenario id is required in the route."
            });
        }

        var result = await _scenarioApplication.ApplyAsync(id, body?.Parameters, cancellationToken).ConfigureAwait(false);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Executes the scenario's PRD-014 <c>flow</c> (sequential edges, real <c>PersonaCall</c> via project-memory LLM). Requires project root.
    /// </summary>
    [HttpPost("{id}/flow/run")]
    [ProducesResponseType(typeof(ScenarioFlowRunResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ScenarioFlowRunResponse>> RunFlowAsync(
        [FromRoute] string id,
        [FromBody] ScenarioFlowRunRequest? body,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(ScenarioFlowRunResponse.Fail("INVALID_SCENARIO_ID", "Scenario id is required in the route."));
        }

        var req = body ?? new ScenarioFlowRunRequest();
        var result = await _flowExecution.RunAsync(id, req, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            var code = result.ErrorCode ?? "FLOW_RUN_FAILED";
            return code switch
            {
                "SCENARIO_NOT_FOUND" => NotFound(result),
                "INTERNAL_ERROR" => StatusCode(StatusCodes.Status500InternalServerError, result),
                _ => BadRequest(result)
            };
        }

        return Ok(result);
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ScenarioDto>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<ScenarioDto>> List()
    {
        var list = _catalog.List().Select(ToDto).ToList();
        return Ok(list);
    }

    /// <summary>Ids from the default catalog that the user chose to hide (see merged <c>GET /api/scenarios</c>).</summary>
    [HttpGet("suppressed-default-ids")]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<string>> ListSuppressedDefaults() => Ok(_catalog.GetSuppressedDefaultScenarioIds());

    [HttpPost]
    [ProducesResponseType(typeof(ScenarioDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ScenarioDto>> CreateScenarioAsync(
        [FromBody] CreateScenarioRequest? body,
        CancellationToken cancellationToken = default)
    {
        if (body == null || string.IsNullOrWhiteSpace(body.Id))
        {
            return BadRequest(new ErrorResponse
            {
                Code = "INVALID_SCENARIO_ID",
                Message = "Request body with a non-empty id is required."
            });
        }

        var id = body.Id.Trim();
        if (!IsAllowedNewScenarioId(id))
        {
            return BadRequest(new ErrorResponse
            {
                Code = "INVALID_SCENARIO_ID",
                Message = "Scenario id must be 1–120 characters: letters, digits, hyphen, underscore, or dot."
            });
        }

        var def = new ScenarioDefinition
        {
            Id = id,
            DisplayName = string.IsNullOrWhiteSpace(body.DisplayName) ? id : body.DisplayName.Trim(),
            Description = string.IsNullOrWhiteSpace(body.Description) ? "" : body.Description.Trim(),
            Kind = ScenarioKinds.Declarative,
            Handler = null,
            AgentTypes = new List<string>(),
            PersonaAgentIds = new List<string>(),
            PersonaBindings = new ScenarioPersonaBindings(),
            Flow = null
        };

        var save = await _catalog.CreateScenarioAsync(def, cancellationToken).ConfigureAwait(false);
        if (!save.Ok)
        {
            return BadRequest(new ErrorResponse
            {
                Code = "SCENARIO_CREATE_FAILED",
                Message = save.Errors.Count > 0 ? string.Join(" ", save.Errors) : "Create failed.",
                Details = save.Errors
            });
        }

        var created = _catalog.Get(id);
        if (created == null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse { Code = "INTERNAL_ERROR", Message = "Scenario was not readable after create." });
        }

        _logger.LogInformation("Scenario {ScenarioId}: created in user catalog", id);
        return CreatedAtAction(nameof(Get), new { id }, ToDto(created));
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteScenarioAsync([FromRoute] string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new ErrorResponse
            {
                Code = "INVALID_SCENARIO_ID",
                Message = "Scenario id is required in the route."
            });
        }

        var save = await _catalog.DeleteScenarioAsync(id, cancellationToken).ConfigureAwait(false);
        if (!save.Ok)
        {
            if (save.Errors.Count == 1 &&
                string.Equals(save.Errors[0], "SCENARIO_NOT_FOUND", StringComparison.Ordinal))
            {
                return NotFound(new ErrorResponse
                {
                    Code = "SCENARIO_NOT_FOUND",
                    Message = $"Scenario '{id.Trim()}' not found."
                });
            }

            return BadRequest(new ErrorResponse
            {
                Code = "SCENARIO_DELETE_FAILED",
                Message = save.Errors.Count > 0 ? string.Join(" ", save.Errors) : "Delete failed.",
                Details = save.Errors
            });
        }

        _logger.LogInformation("Scenario {ScenarioId}: removed or suppressed in user catalog", id.Trim());
        return NoContent();
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ScenarioDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public ActionResult<ScenarioDto> Get([FromRoute] string id)
    {
        var def = _catalog.Get(id);
        if (def == null)
        {
            return NotFound(new ErrorResponse { Code = "SCENARIO_NOT_FOUND", Message = $"Scenario '{id}' not found." });
        }

        return Ok(ToDto(def));
    }

    /// <summary>
    /// Persists <c>flow</c> for one scenario to <c>agctor-scenarios.user.json</c> (upsert); validates the full merged catalog.
    /// </summary>
    [HttpPut("{id}/flow")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PutFlowAsync(
        [FromRoute] string id,
        [FromBody] ScenarioFlowDocument? flow,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new ErrorResponse
            {
                Code = "INVALID_SCENARIO_ID",
                Message = "Scenario id is required in the route."
            });
        }

        var save = await _catalog.SaveScenarioFlowAsync(id, flow, cancellationToken).ConfigureAwait(false);
        if (!save.Ok)
        {
            if (save.Errors.Count == 1 &&
                string.Equals(save.Errors[0], "SCENARIO_NOT_FOUND", StringComparison.Ordinal))
            {
                return NotFound(new ErrorResponse
                {
                    Code = "SCENARIO_NOT_FOUND",
                    Message = $"Scenario '{id.Trim()}' not found."
                });
            }

            return BadRequest(new ErrorResponse
            {
                Code = "SCENARIO_FLOW_VALIDATION_FAILED",
                Message = "Flow save validation failed.",
                Details = save.Errors
            });
        }

        _logger.LogInformation("Scenario {ScenarioId}: flow persisted to user catalog file", id.Trim());
        return NoContent();
    }

    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Put([FromBody] ScenarioCatalogUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var doc = new ScenarioCatalogDocument
        {
            Version = request?.Version ?? 1,
            Scenarios = (request?.Scenarios ?? new List<ScenarioDto>()).Select(ToDefinition).ToList(),
            SuppressedDefaultScenarioIds = request?.SuppressedDefaultScenarioIds
        };

        var save = await _catalog.SaveAsync(doc, cancellationToken).ConfigureAwait(false);
        if (!save.Ok)
        {
            return BadRequest(new ErrorResponse
            {
                Code = "SCENARIO_VALIDATION_FAILED",
                Message = "Scenario catalog validation failed.",
                Details = save.Errors
            });
        }

        _logger.LogInformation("Scenario catalog updated with {Count} entries", doc.Scenarios.Count);
        return NoContent();
    }

    [HttpPost("reload")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Reload(CancellationToken cancellationToken = default)
    {
        await _catalog.ReloadAsync(cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    private static ScenarioDto ToDto(ScenarioDefinition d) => new()
    {
        Id = d.Id,
        DisplayName = d.DisplayName,
        Description = d.Description,
        Kind = d.Kind,
        Handler = d.Handler,
        AgentTypes = d.AgentTypes?.ToList() ?? new List<string>(),
        PersonaAgentIds = d.PersonaAgentIds?.ToList() ?? new List<string>(),
        PersonaBindings = new ScenarioPersonaBindingsDto
        {
            Extractor = d.PersonaBindings?.Extractor,
            Curator = d.PersonaBindings?.Curator,
            Query = d.PersonaBindings?.Query
        },
        Flow = ScenarioFlowDocument.Clone(d.Flow)
    };

    private static bool IsAllowedNewScenarioId(string id)
    {
        if (id.Length is < 1 or > 120)
            return false;
        foreach (var c in id)
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_' or '.')
                continue;
            return false;
        }

        return true;
    }

    private static ScenarioDefinition ToDefinition(ScenarioDto d) => new()
    {
        Id = d.Id,
        DisplayName = d.DisplayName,
        Description = d.Description,
        Kind = d.Kind,
        Handler = d.Handler,
        AgentTypes = d.AgentTypes?.ToList() ?? new List<string>(),
        PersonaAgentIds = d.PersonaAgentIds?.ToList() ?? new List<string>(),
        PersonaBindings = new ScenarioPersonaBindings
        {
            Extractor = d.PersonaBindings?.Extractor,
            Curator = d.PersonaBindings?.Curator,
            Query = d.PersonaBindings?.Query
        },
        Flow = ScenarioFlowDocument.Clone(d.Flow)
    };
}

