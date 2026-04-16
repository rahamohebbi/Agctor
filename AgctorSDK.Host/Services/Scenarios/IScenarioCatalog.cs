namespace AgctorSDK.Host.Services.Scenarios;

/// <summary>
/// Loads and validates scenario definitions from JSON files.
/// </summary>
public interface IScenarioCatalog
{
    IReadOnlyList<ScenarioDefinition> List();
    ScenarioDefinition? Get(string id);

    /// <summary>Ids from the default catalog excluded by the user file (<see cref="ScenarioCatalogDocument.SuppressedDefaultScenarioIds"/>).</summary>
    IReadOnlyList<string> GetSuppressedDefaultScenarioIds();

    Task ReloadAsync(CancellationToken cancellationToken = default);
    Task<(bool Ok, IReadOnlyList<string> Errors)> SaveAsync(ScenarioCatalogDocument userDocument, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts one scenario's <see cref="ScenarioDefinition.Flow"/> into the user catalog file and re-merges with defaults.
    /// Validates the full merged catalog after the change.
    /// </summary>
    Task<(bool Ok, IReadOnlyList<string> Errors)> SaveScenarioFlowAsync(
        string scenarioId,
        ScenarioFlowDocument? flow,
        CancellationToken cancellationToken = default);

    /// <summary>Appends a declarative scenario to the user catalog file (must not already exist in the merged catalog).</summary>
    Task<(bool Ok, IReadOnlyList<string> Errors)> CreateScenarioAsync(
        ScenarioDefinition scenario,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a scenario from the user file; if it still exists in defaults, records a suppression id.</summary>
    Task<(bool Ok, IReadOnlyList<string> Errors)> DeleteScenarioAsync(
        string scenarioId,
        CancellationToken cancellationToken = default);
}

