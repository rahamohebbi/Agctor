using AgctorSDK.Host.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace AgctorSDK.Host.Services;

/// <inheritdoc />
public sealed class ScenarioApplicationService : IScenarioApplicationService
{
    private readonly IScenarioFactory _scenarioFactory;
    private readonly ICurrentScenarioStore _currentScenarioStore;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ScenarioApplicationService> _logger;

    public ScenarioApplicationService(
        IScenarioFactory scenarioFactory,
        ICurrentScenarioStore currentScenarioStore,
        IConfiguration configuration,
        ILogger<ScenarioApplicationService> logger)
    {
        _scenarioFactory = scenarioFactory ?? throw new ArgumentNullException(nameof(scenarioFactory));
        _currentScenarioStore = currentScenarioStore ?? throw new ArgumentNullException(nameof(currentScenarioStore));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<(int StatusCode, object Body)> ApplyAsync(
        string? scenarioIdOrNull,
        Dictionary<string, object>? parameters,
        CancellationToken cancellationToken)
    {
        var scenarioName = ResolveScenarioName(scenarioIdOrNull);
        _logger.LogInformation("Applying scenario: {ScenarioName}", scenarioName);

        try
        {
            if (string.IsNullOrWhiteSpace(scenarioName))
            {
                return (400, new ErrorResponse
                {
                    Code = "INVALID_SCENARIO_NAME",
                    Message = "Scenario name is required (set Agctor:Dashboard:ScenarioName or pass a scenario id; use id \"default\" for configured default)."
                });
            }

            var scenario = _scenarioFactory.GetScenario(scenarioName);
            if (scenario == null)
            {
                var availableScenarios = string.Join(", ", _scenarioFactory.GetAvailableScenarios());
                return (400, new ErrorResponse
                {
                    Code = "UNKNOWN_SCENARIO",
                    Message = $"Unknown scenario '{scenarioName}'. Available scenarios: {availableScenarios}"
                });
            }

            var response = await scenario.SetupAsync(parameters).ConfigureAwait(false);

            if (response.Success)
            {
                _currentScenarioStore.SetCurrentScenario(scenarioName, scenario.Description);
                _logger.LogInformation("Successfully applied scenario '{ScenarioName}' with {AgentCount} agents",
                    scenarioName, response.CreatedAgentIds.Count);
            }
            else
            {
                _logger.LogWarning("Scenario '{ScenarioName}' setup reported failure: {Error}",
                    scenarioName, response.ErrorMessage);
            }

            return (200, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying scenario {ScenarioName}", scenarioName);
            return (500, new ErrorResponse
            {
                Code = "INTERNAL_ERROR",
                Message = "An internal error occurred while setting up the scenario"
            });
        }
    }

    private string ResolveScenarioName(string? scenarioIdOrNull)
    {
        if (string.IsNullOrWhiteSpace(scenarioIdOrNull))
            return _configuration.GetValue<string>("Agctor:Dashboard:ScenarioName") ?? "";
        if (string.Equals(scenarioIdOrNull.Trim(), "default", StringComparison.OrdinalIgnoreCase))
            return _configuration.GetValue<string>("Agctor:Dashboard:ScenarioName") ?? "";
        return scenarioIdOrNull.Trim();
    }
}

/// <summary>Maps apply tuple to MVC results (shared by Test + Scenarios controllers).</summary>
public static class ScenarioApplyActionMapper
{
    public static ActionResult<ScenarioSetupResponse> ToActionResult(this (int StatusCode, object Body) r, ControllerBase c)
    {
        return r.StatusCode switch
        {
            200 => c.Ok((ScenarioSetupResponse)r.Body),
            400 => c.BadRequest((ErrorResponse)r.Body),
            500 => c.StatusCode(500, (ErrorResponse)r.Body),
            _ => c.StatusCode(r.StatusCode, r.Body)
        };
    }
}
