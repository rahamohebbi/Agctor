using Microsoft.AspNetCore.Mvc;
using AgctorSDK.Host.Models;
using AgctorSDK.Host.Services;
using Microsoft.Extensions.Configuration;

namespace AgctorSDK.Host.Controllers;

/// <summary>
/// Controller for test scenarios and development utilities.
/// Provides endpoints for setting up predefined agent configurations for testing.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class TestController : ControllerBase
{
    private readonly IScenarioFactory _scenarioFactory;
    private readonly ICurrentScenarioStore _currentScenarioStore;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TestController> _logger;

    public TestController(
        IScenarioFactory scenarioFactory,
        ICurrentScenarioStore currentScenarioStore,
        IConfiguration configuration,
        ILogger<TestController> logger)
    {
        _scenarioFactory = scenarioFactory ?? throw new ArgumentNullException(nameof(scenarioFactory));
        _currentScenarioStore = currentScenarioStore ?? throw new ArgumentNullException(nameof(currentScenarioStore));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Sets up a predefined test scenario by creating the required agents and configurations.
    /// </summary>
    /// <param name="request">The scenario setup request containing scenario name and parameters</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>Response indicating scenario setup status and created agents</returns>
    /// <response code="200">Scenario was successfully set up</response>
    /// <response code="400">Invalid request format or unknown scenario</response>
    /// <response code="500">Internal server error occurred during setup</response>
    [HttpPost("setup-scenario")]
    [ProducesResponseType(typeof(ScenarioSetupResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ScenarioSetupResponse>> SetupScenarioAsync(
        [FromBody] ScenarioSetupRequest? request,
        CancellationToken cancellationToken = default)
    {
        request ??= new ScenarioSetupRequest(null, null);
        var scenarioName = request.ScenarioName;
        if (string.IsNullOrWhiteSpace(scenarioName))
            scenarioName = _configuration.GetValue<string>("Agctor:Dashboard:ScenarioName") ?? "";

        _logger.LogInformation("Setting up test scenario: {ScenarioName}", scenarioName);

        try
        {
            if (string.IsNullOrWhiteSpace(scenarioName))
            {
                return BadRequest(new ErrorResponse
                {
                    Code = "INVALID_SCENARIO_NAME",
                    Message = "Scenario name is required (set Agctor:Dashboard:ScenarioName or pass scenarioName in the body)."
                });
            }

            // Get the scenario
            var scenario = _scenarioFactory.GetScenario(scenarioName);
            if (scenario == null)
            {
                var availableScenarios = string.Join(", ", _scenarioFactory.GetAvailableScenarios());
                return BadRequest(new ErrorResponse
                {
                    Code = "UNKNOWN_SCENARIO",
                    Message = $"Unknown scenario '{scenarioName}'. Available scenarios: {availableScenarios}"
                });
            }

            // Set up the scenario
            var response = await scenario.SetupAsync(request.Parameters);

            if (response.Success)
            {
                _currentScenarioStore.SetCurrentScenario(scenarioName, scenario.Description);
                _logger.LogInformation("Successfully set up scenario '{ScenarioName}' with {AgentCount} agents",
                    scenarioName, response.CreatedAgentIds.Count);
            }
            else
            {
                _logger.LogWarning("Failed to set up scenario '{ScenarioName}': {Error}",
                    scenarioName, response.ErrorMessage);
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting up scenario {ScenarioName}", scenarioName);
            return StatusCode(500, new ErrorResponse
            {
                Code = "INTERNAL_ERROR",
                Message = "An internal error occurred while setting up the scenario"
            });
        }
    }

    /// <summary>
    /// Gets a list of all available test scenarios.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>List of available scenarios with descriptions</returns>
    /// <response code="200">Successfully retrieved scenario list</response>
    /// <response code="500">Internal server error occurred</response>
    [HttpGet("scenarios")]
    [ProducesResponseType(typeof(Dictionary<string, string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public ActionResult<Dictionary<string, string>> GetAvailableScenarios(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Retrieving available test scenarios");

        try
        {
            var scenarios = _scenarioFactory.GetScenarioDescriptions();
            _logger.LogInformation("Retrieved {ScenarioCount} available scenarios", scenarios.Count);
            return Ok(scenarios);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving available scenarios");
            return StatusCode(500, new ErrorResponse
            {
                Code = "INTERNAL_ERROR",
                Message = "An internal error occurred while retrieving scenarios"
            });
        }
    }

    /// <summary>
    /// Gets detailed information about a specific test scenario.
    /// </summary>
    /// <param name="scenarioName">The name of the scenario to get information about</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>Detailed scenario information</returns>
    /// <response code="200">Successfully retrieved scenario information</response>
    /// <response code="404">Scenario not found</response>
    /// <response code="500">Internal server error occurred</response>
    [HttpGet("scenarios/{scenarioName}")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public ActionResult<object> GetScenarioInfo(
        [FromRoute] string scenarioName,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Retrieving information for scenario: {ScenarioName}", scenarioName);

        try
        {
            if (string.IsNullOrWhiteSpace(scenarioName))
            {
                return BadRequest(new ErrorResponse
                {
                    Code = "INVALID_SCENARIO_NAME",
                    Message = "Scenario name cannot be null or empty"
                });
            }

            var scenario = _scenarioFactory.GetScenario(scenarioName);
            if (scenario == null)
            {
                return NotFound(new ErrorResponse
                {
                    Code = "SCENARIO_NOT_FOUND",
                    Message = $"Scenario '{scenarioName}' not found"
                });
            }

            var scenarioInfo = new
            {
                name = scenario.Name,
                description = scenario.Description,
                // Add more details as needed
                supportedParameters = new List<string>(), // Placeholder
                exampleUsage = new
                {
                    endpoint = "POST /api/Test/setup-scenario",
                    body = new
                    {
                        scenarioName = scenario.Name,
                        parameters = new Dictionary<string, object>()
                    }
                }
            };

            return Ok(scenarioInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving scenario information for {ScenarioName}", scenarioName);
            return StatusCode(500, new ErrorResponse
            {
                Code = "INTERNAL_ERROR",
                Message = "An internal error occurred while retrieving scenario information"
            });
        }
    }

    /// <summary>
    /// Gets the scenario that is currently active in this session (last applied via Apply on the dashboard).
    /// </summary>
    /// <returns>Current scenario name and description, or null if none has been applied</returns>
    /// <response code="200">Current scenario info or null</response>
    [HttpGet("current-scenario")]
    [ProducesResponseType(typeof(CurrentScenarioResponse), StatusCodes.Status200OK)]
    public ActionResult<CurrentScenarioResponse?> GetCurrentScenario()
    {
        var name = _currentScenarioStore.GetCurrentScenarioName();
        if (string.IsNullOrEmpty(name))
            return Ok((CurrentScenarioResponse?)null);

        return Ok(new CurrentScenarioResponse(name, _currentScenarioStore.GetCurrentScenarioDescription()));
    }
} 