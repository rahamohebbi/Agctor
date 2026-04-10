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
    private readonly ILogger<ScenariosController> _logger;

    public ScenariosController(
        IScenarioCatalog catalog,
        IScenarioApplicationService scenarioApplication,
        ILogger<ScenariosController> logger)
    {
        _catalog = catalog;
        _scenarioApplication = scenarioApplication;
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

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ScenarioDto>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<ScenarioDto>> List()
    {
        var list = _catalog.List().Select(ToDto).ToList();
        return Ok(list);
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

    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Put([FromBody] ScenarioCatalogUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var doc = new ScenarioCatalogDocument
        {
            Version = request?.Version ?? 1,
            Scenarios = (request?.Scenarios ?? new List<ScenarioDto>()).Select(ToDefinition).ToList()
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
        }
    };

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
        }
    };
}

