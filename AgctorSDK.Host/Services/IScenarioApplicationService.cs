using AgctorSDK.Host.Models;

namespace AgctorSDK.Host.Services;

/// <summary>
/// Shared scenario apply path for <c>POST /api/scenarios/{id}/apply</c> and legacy <c>POST /api/Test/setup-scenario</c> (PRD-013 Phase 4).
/// </summary>
public interface IScenarioApplicationService
{
    /// <summary>
    /// Resolves scenario id: null/whitespace or literal <c>default</c> → <c>Agctor:Dashboard:ScenarioName</c>.
    /// </summary>
    /// <returns>HTTP status and payload: <see cref="ScenarioSetupResponse"/> on 200, <see cref="ErrorResponse"/> on errors.</returns>
    Task<(int StatusCode, object Body)> ApplyAsync(
        string? scenarioIdOrNull,
        Dictionary<string, object>? parameters,
        CancellationToken cancellationToken);
}
